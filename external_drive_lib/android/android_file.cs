﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using external_drive_lib.interfaces;
using Shell32;

namespace external_drive_lib.android
{
    internal class android_file : IFile
    {
        private FolderItem fi_;
        public android_file(FolderItem fi) {
            fi_ = fi;
        }

        public string name { get; }
        public IFolder folder { get; }
        public string full_path { get; }
        public void copy(string dest_path) {
        }

        public void delete() {
        }
    }
}
