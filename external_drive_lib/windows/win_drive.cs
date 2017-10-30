﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using external_drive_lib.exceptions;
using external_drive_lib.interfaces;

namespace external_drive_lib.windows
{
    class win_drive : IDrive {
        private static log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private string root_;
        private bool valid_ = true;

        public win_drive(DriveInfo di) {
            try {
                root_ = di.RootDirectory.FullName;
            } catch (Exception e) {
                logger.Error("bad drive " + di + " : " + e);
                valid_ = false;
            }
        }

        public win_drive(string root) {
            root_ = root;
        }

        public bool is_connected() {
            return false;
        }

        public drive_type type {
            get { return drive_type.hdd; }
        }

        public string root_name {
            get { return root_; }
        }

        public string unique_id {
            get { return root_; }
        } 
        public string friendly_name {
            get { return root_; }
        }

        public IEnumerable<IFolder> folders {
            get { return new DirectoryInfo(root_).EnumerateDirectories().Select(f => new win_folder(root_, f.Name)); }
        }
        public IEnumerable<IFile> files {
            get { return new DirectoryInfo(root_).EnumerateFiles().Select(f => new win_file(root_, f.Name)); }
        }

        public IFile parse_file(string path) {
            var full = root_ + path;
            if (File.Exists(full)) {
                var fi = new FileInfo(full);
                return new win_file(fi.DirectoryName, fi.Name);
            }
            throw new exception("not an existing file " + full);
        }

        public IFolder parse_folder(string path) {
            var full = root_ + path;
            if (Directory.Exists(full)) {
                var fi = new DirectoryInfo(full);
                return new win_folder(fi.Parent.FullName, fi.Name);
            }
            throw new exception("not an existing folder " + full);
        }
    }
}
