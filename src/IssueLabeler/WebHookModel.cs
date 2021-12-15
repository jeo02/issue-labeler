using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IssueLabeler
{
    internal class WebHookModel
    {
        public string owner { get; set; }
        public string repo { get; set; }
        public int id { get; set; }
    }
}
