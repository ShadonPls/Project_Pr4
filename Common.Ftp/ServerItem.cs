using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Ftp
{
    public class ServerItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public string Size { get; set; }
        public string Icon { get; set; }
        public ObservableCollection<ServerItem> Children { get; set; }
        public bool IsExpanded { get; set; }
        public bool HasChildren { get; set; }

        public ServerItem()
        {
            Children = new ObservableCollection<ServerItem>();
        }
    }
}
