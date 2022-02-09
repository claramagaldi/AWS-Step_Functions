using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GCImagingAWSLambdaS3;
using Microsoft.Extensions.Configuration;
using Amazon.Lambda.Serialization.Json;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Assignment4_Clara
{
    public class StepFunctionTasks
    {
        public StepFunctionTasks()
        {
        }

        public State IsImage(State state, ILambdaContext context)
        {
            string photo = state.Photo;
            photo = photo.Substring(photo.LastIndexOf('/') + 1);
            state.Photo = photo;

            string ext = photo.Substring(photo.LastIndexOf('.') + 1);
            if(ext == "jpg" || ext == "jpeg" || ext == "png")
            {
                state.IsImage = 1;
            } else
            {
                state.IsImage = 0;
            }
            return state;
        }

        public async Task<State> LabelsAndDynamoDBAsync(State state, ILambdaContext context)
        {
            await DetectLabelAsync(state.Photo, state.Bucket);
            return state;
        }

        public async Task<State> ThumbnailAndS3Async(State state, ILambdaContext context)
        {
            IAmazonS3 S3Client = new AmazonS3Client(AWScredentials(), RegionEndpoint.USEast1);
            var rs = await S3Client.GetObjectMetadataAsync(state.Bucket, "Originals/" + state.Photo);

            if (rs.Headers.ContentType.StartsWith("image/"))
            {
                using (GetObjectResponse response = await S3Client.GetObjectAsync(state.Bucket, "Originals/" + state.Photo))
                {
                    using (Stream responseStream = response.ResponseStream)
                    {
                        using (StreamReader reader = new StreamReader(responseStream))
                        {
                            using (var memstream = new MemoryStream())
                            {
                                var buffer = new byte[512];
                                var bytesRead = default(int);
                                while ((bytesRead = reader.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
                                    memstream.Write(buffer, 0, bytesRead);
                                // Perform image manipulation 
                                var transformedImage = GcImagingOperations.GetConvertedImage(memstream.ToArray());
                                PutObjectRequest putRequest = new PutObjectRequest()
                                {
                                    BucketName = state.Bucket + "/Thumbnails",
                                    Key = $"grayscale-{state.Photo}",
                                    ContentType = rs.Headers.ContentType,
                                    ContentBody = transformedImage
                                };
                                await S3Client.PutObjectAsync(putRequest);
                            }
                        }
                    }
                }
            }
            return state;
        }

        public State Success(State state, ILambdaContext context)
        {
            return state;
        }

        public static System.Collections.Specialized.NameValueCollection AppSettings { get; }

        private static BasicAWSCredentials AWScredentials()
        {
            //string path = Directory.GetCurrentDirectory();
            //string newPath = Path.GetFullPath(Path.Combine(path, @"..\..\"));

            //var builder = new ConfigurationBuilder()
            //.SetBasePath(newPath)
            //.AddJsonFile("AppSettings.json", optional: true, reloadOnChange: true);

            var accessKeyID = "YOUR KEY";//builder.Build().GetSection("AWSCredentials").GetSection("AccesskeyID").Value;  //System.Configuration.ConfigurationManager.AppSettings["accessKeyID"]; //
            var secretKey = "YOUR SECRET KEY";//builder.Build().GetSection("AWSCredentials").GetSection("Secretaccesskey").Value; //System.Configuration.ConfigurationManager.AppSettings["secretKey"]; //

            return new BasicAWSCredentials(accessKeyID, secretKey);
        }

        private async Task DetectLabelAsync(string photo, string bucket)
        {
            AmazonRekognitionClient rekognitionClient = new AmazonRekognitionClient(AWScredentials(), RegionEndpoint.USEast1);
            DetectLabelsRequest detectlabelsRequest = new DetectLabelsRequest()
            {
                Image = new Image()
                {
                    S3Object = new Amazon.Rekognition.Model.S3Object()
                    {
                        Name = "Originals/" + photo,
                        Bucket = bucket
                    },
                },
                MinConfidence = 90F
            };

            try
            {
                
                DetectLabelsResponse detectLabelsResponse = await rekognitionClient.DetectLabelsAsync(detectlabelsRequest);

                AmazonDynamoDBClient clientDB = new AmazonDynamoDBClient(AWScredentials(), Amazon.RegionEndpoint.USEast1);
                Table UserTable = Table.LoadTable(clientDB, "Labels", DynamoDBEntryConversion.V2);
                Document newPhoto = new Document();
                newPhoto["URL"] = "https://" + bucket + ".s3.amazonaws.com/Originals/" + photo;
                Document newMap = new Document();
                int count = 1;

                foreach (Label label in detectLabelsResponse.Labels)
                {
                    Document newLabel = new Document();
                    newLabel["Name"] = label.Name;
                    newLabel["Confidence"] = label.Confidence;
                    newMap[count.ToString()] = newLabel;
                    count++;
                }
                newPhoto["Labels"] = newMap;
                await UserTable.PutItemAsync(newPhoto);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
