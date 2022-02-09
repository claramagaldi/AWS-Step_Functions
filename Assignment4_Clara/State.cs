using System;
using System.Collections.Generic;
using System.Text;

namespace Assignment4_Clara
{
    /// <summary>
    /// The state passed between the step function executions.
    /// </summary>
    public class State
    {
        public string Photo { get; set; }

        public string Bucket { get; set; }

        public int IsImage { get; set; }
    }
}
