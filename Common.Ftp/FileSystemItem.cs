using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Ftp
{
    public class FileSystemItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Size { get; set; }
        public DateTime LastModified { get; set; }
    }
}
