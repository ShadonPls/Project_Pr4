using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Ftp
{
    public class FileInfoFTP
    {
        public byte[] Data {  get; set; }
        public string Name { get; set; }
        public FileInfoFTP (byte[] data, string name)
        {
            Data = data;
            Name = name;
        }
    }
}
