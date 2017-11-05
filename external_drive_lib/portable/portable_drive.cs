﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using external_drive_lib.exceptions;
using external_drive_lib.interfaces;
using external_drive_lib.util;
using Shell32;

namespace external_drive_lib.portable
{
    internal class portable_drive : IDrive {
        private const int RETRY_TIMES = 5;
        private const int SLEEP_BEFORE_RETRY_MS = 200;

        private FolderItem root_;
        private drive_type drive_type_;

        private string friendly_name_;

        private string root_path_;
        private string vid_pid_ = "";

        private string unique_id_ = "";

        private bool enumerated_children_ = false;
        private List<IFolder> folders_ = new List<IFolder>();
        private List<IFile> files_ = new List<IFile>();

        private bool connected_via_usb_ = true;

        public portable_drive(FolderItem fi) {
            root_ = fi;
            friendly_name_ = root_.Name;
            root_path_ = root_.Path;

            var idx = root_path_.IndexOf("vid_");
            Debug.Assert(idx >= 0);
            if (idx >= 0) {
                var idx2 = root_path_.IndexOf("pid_", idx);
                var idx3 = root_path_.IndexOf("&", idx2);
                vid_pid_ = root_path_.Substring(idx, idx3 - idx);
            }
            unique_id_ = unique_id;

            find_drive_type();
        }

        private void find_drive_type() {
            drive_type_ = drive_type.android;
            if (root_.IsFolder) {
                var items = (root_.GetFolder as Folder).Items();
                if (items.Count == 1) {
                    var child = items.Item(0) as FolderItem;
                    if (child.IsFolder) {
                        if (child.Name == "Phone")
                            drive_type_ = drive_type.android_phone;
                        else if (child.Name == "Tablet")
                            drive_type_ = drive_type.android_tablet;
                    }
                }
            }
        }

        internal bool connected_via_usb {
            get { return connected_via_usb_; }
            set { connected_via_usb_ = value; }
        }

        public bool is_connected() {
            try {
                if (connected_via_usb) {
                    var items = (root_.GetFolder as Folder).Items();
                    return items.Count >= 1;
                }
            } catch {
            }
            return false;
        }

        public drive_type type {
            get { return drive_type_; }
        }

        public string root_name {
            get { return root_path_; }
        }
        
        public IEnumerable<IFolder> folders {
            get {
                if (!enumerated_children_) {
                    enumerated_children_ = true;
                    portable_util.enumerate_children(this, root_, folders_, files_);
                }
                return folders_;
            }
        }
        public IEnumerable<IFile> files {
            get {
                if (!enumerated_children_) {
                    enumerated_children_ = true;
                    portable_util.enumerate_children(this, root_, folders_, files_);
                }
                return files_;                
            }
        }

        // FIXME
        public string unique_id {
            get { return unique_id_; }
            internal set { unique_id_ = value; }
        }

        public string friendly_name {
            get { return friendly_name_; }
        }

        public string vid_pid {
            get { return vid_pid_; }
        }

        private FolderItem parse_sub_folder(IEnumerable<string> sub_folder_path) {
            var cur_folder = root_.GetFolder as Folder;
            var cur_folder_item = root_;
            var idx = 0;
            foreach (var sub in sub_folder_path) {
                if (idx == 0 && sub == "*") {
                    // special case - replace with single root folder
                    var sub_items = cur_folder.Items();
                    if (sub_items.Count == 1 && sub_items.Item(0).IsFolder) 
                        cur_folder = sub_items.Item(0).GetFolder as Folder;
                    else 
                        throw new exception("Root drive doesn't have a single root folder (*)");
                } else {
                    var sub_folder = cur_folder.ParseName(sub);
                    if (sub_folder == null)
                        return null;
                    cur_folder_item = sub_folder;
                    cur_folder = cur_folder_item.GetFolder as Folder;
                }
                ++idx;
            }
            return cur_folder_item;
        }

        public IFile parse_file(string path) {
            var unique_drive_id = "{" + unique_id + "}";
            if (path.StartsWith(unique_drive_id, StringComparison.CurrentCultureIgnoreCase))
                path = path.Substring(unique_drive_id.Length + 2); // ignore ":\" as well
            if (path.StartsWith(root_path_, StringComparison.CurrentCultureIgnoreCase))
                path = path.Substring(root_path_.Length + 1);

            var sub_folder_names = path.Replace("/", "\\").Split('\\').ToList();
            var file_name = sub_folder_names.Last();
            sub_folder_names.RemoveAt(sub_folder_names.Count - 1);
            var raw_folder = parse_sub_folder(sub_folder_names);
            if (raw_folder == null)
                throw new exception("invalid path " + path);
            var file = (raw_folder.GetFolder as Folder).ParseName(file_name);
            if ( file == null)
                throw new exception("invalid path " + path);
            return new portable_file(this, file as FolderItem2);
        }

        public IFolder parse_folder(string path) {
            path = path.Replace("/", "\\");
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);
            var unique_drive_id = "{" + unique_id + "}";
            if (path.StartsWith(unique_drive_id, StringComparison.CurrentCultureIgnoreCase))
                path = path.Substring(unique_drive_id.Length + 2); // ignore ":\" as well
            if (path.StartsWith(root_path_, StringComparison.CurrentCultureIgnoreCase))
                path = path.Substring(root_path_.Length + 1);

            var sub_folder_names = path.Split('\\').ToList();
            var raw_folder = parse_sub_folder(sub_folder_names);
            if (raw_folder == null)
                throw new exception("invalid path " + path);
            return new portable_folder(this, raw_folder);
        }

        public string parse_android_path(FolderItem fi) {
            var path = fi.Path;
            if (path.EndsWith("\\"))
                path = path.Substring(path.Length - 1);
            Debug.Assert(path.StartsWith(root_name, StringComparison.CurrentCultureIgnoreCase));
            // ignore the drive + "\\"
            path = path.Substring(root_name.Length + 1);
            var sub_folder_count = path.Count(c => c == '\\') + 1;
            var cur = fi;
            var name = "";
            for (int i = 0; i < sub_folder_count; ++i) {
                if (name != "")
                    name = "\\" + name;
                name = cur.Name + name;
                cur = (cur.Parent as Folder2).Self;
            }

            name = "{" + unique_id + "}:\\" + name;
            return name;
        }

        // if folder already exists, it returns it
        public IFolder create_folder(string path) {
            path = path.Replace("/", "\\");
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);
            var id = "{" + unique_id + "}:\\";
            var contains_drive_prefix = path.StartsWith(id, StringComparison.CurrentCultureIgnoreCase);
            if (contains_drive_prefix)
                path = path.Substring(id.Length);

            var cur = root_;
            var sub_folders = path.Split('\\');
            foreach (var sub_name in sub_folders) {
                var folder_object = cur.GetFolder as Folder;
                var sub = folder_object.ParseName(sub_name);
                if (sub == null) {
                    folder_object.NewFolder(sub_name);
                    sub = folder_object.ParseName(sub_name);
                }
                if ( sub == null)
                    throw new exception("could not create part of path " + path);

                if ( !sub.IsFolder)
                    throw new exception("part of path is a file: " + path);
                cur = sub;
            }

            return new portable_folder(this, cur);
        }
    }
}
