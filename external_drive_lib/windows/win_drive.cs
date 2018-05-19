﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using external_drive_lib.exceptions;
using external_drive_lib.interfaces;
using external_drive_lib.util;

namespace external_drive_lib.windows
{
    /* note: the main reason we have win drives is so that you can copy from a windows drive to an android drive
     */
    class win_drive : IDrive {

        private string root_;
        private bool valid_ = true;

        private drive_type drive_type_ = drive_type.internal_hdd;

        private string friendly_name_ = "";

        private string cached_unique_id_ = null;

        public win_drive(DriveInfo di) {
            try {
                root_ = di.RootDirectory.FullName;
                drive_type_ = find_drive_type(di);
                friendly_name_ = find_friendly_name(di);
                check_if_still_connected();
            } catch (Exception e) {
                // "bad drive " + di + " : " + e;
                valid_ = false;
            }
        }

        public win_drive(string root) {
            root_ = root;
        }

        public bool is_connected() {
            return check_if_still_connected();
        }

        public bool is_available() {
            return check_if_still_connected();
        }

        public drive_type type {
            get { return drive_type_; }
        }

        public string root_name {
            get { return root_; }
        }

        public string unique_id {
            get {
                string id = null;
                if (cached_unique_id_ == null) {
                    id = find_unique_id();
                    if (id != null) {
                        lock (this)
                            cached_unique_id_ = id;
                        return id;
                    }
                } else
                    return cached_unique_id_;

                return root_;
            }
        } 
        public string friendly_name {
            get { return friendly_name_; }
        }

        private string find_unique_id() {
            if (drive_type_ != drive_type.cd_rom && drive_type_ != drive_type.internal_hdd) {
                var id = drive_root.inst.try_get_unique_id_for_drive(root_[0]);
                // could be null
                return id;
            }

            return root_;
        }

        private bool check_if_still_connected() {
            if (drive_type_ == drive_type.cd_rom || drive_type_ == drive_type.internal_hdd)
                return true;

            // as soon as we know the driv'e unique id, find it
            var id = drive_root.inst.try_get_unique_id_for_drive(root_[0]);
            if ( cached_unique_id_ == null && id != null)
                lock (this)
                    cached_unique_id_ = id;
            return id == cached_unique_id_;
        }

        private string find_friendly_name(DriveInfo di) {
            var not_allowed = "~#%&*{}\\:<>?/+|\"";
            switch (drive_type_) {
                case drive_type.sd_card:
                case drive_type.usb_stick:
                    var volume = di.VolumeLabel;
                    if (!string.IsNullOrEmpty(volume))
                        volume = new string( volume.Trim().Where(ch => !not_allowed.Contains(ch)).ToArray());
                    if (!string.IsNullOrEmpty(volume))
                        return volume;
                    break;
            }

            switch (drive_type_) {
                case drive_type.sd_card:
                case drive_type.usb_stick:
                    var possible_name = drive_frienly_name(di.Name);
                    if (possible_name != null)
                        return possible_name;
                    break;
            }

            switch (drive_type_) {
                case drive_type.sd_card:
                    return "SD Card";
                case drive_type.usb_stick:
                    return "USB Stick";
            }
            return root_;            
        }

        private drive_type find_drive_type(DriveInfo di) {
            switch (di.DriveType) {
                case DriveType.Unknown:
                case DriveType.NoRootDirectory:
                    return drive_type.unknown;

                case DriveType.Network:
                    return drive_type.network;
                case DriveType.CDRom:
                    return drive_type.cd_rom;

                case DriveType.Removable:
                    var is_usb = is_usb_drive(di.Name);
                    return is_usb ? drive_type.usb_stick : drive_type.sd_card;

                case DriveType.Fixed:
                    var is_external = is_external_disk(di.Name);
                    return is_external ? drive_type.external_hdd : drive_type.internal_hdd;

                case DriveType.Ram:
                    return drive_type.internal_hdd;

                default:
                    Debug.Assert(false);
                    return drive_type.unknown;
            }
        }

        private static string drive_frienly_name(string drive) {
            try {
                var found = portable_util.get_all_connected_device_drives().FirstOrDefault(d => d.Path.ToLower() == drive.ToLower());
                if (found != null) {
                    var friendly = found.Name;
                    if (friendly.IndexOf("(") >= 0)
                        friendly = friendly.Substring(0, friendly.IndexOf("(")).Trim();
                    if ( friendly != "")
                        return friendly;
                }
            } catch {
            }
            return null;
        }

        private static bool is_usb_drive(string drive) {
            try {
                var found = portable_util.get_all_connected_device_drives().FirstOrDefault(d => d.Path.ToLower() == drive.ToLower());
                if (found != null) {
                    return found.Type.ToLower().Contains("usb");
                }
            } catch {
            }
            return false;
        }

        //https://stackoverflow.com/questions/9891854/how-to-determine-if-drive-is-external-drive
        private static bool is_external_disk(string drive_letter) {
            drive_letter = drive_letter.TrimEnd('\\');

            // just in case we get exceptions (can happen on drive taken out)
            for (int retry = 0; retry < 3; ++retry)
                try {
                    // browse all USB WMI physical disks
                    foreach (ManagementObject drive in new ManagementObjectSearcher("select DeviceID, MediaType,InterfaceType from Win32_DiskDrive").Get()) {
                        // associate physical disks with partitions
                        ManagementObjectCollection partitionCollection =
                            new ManagementObjectSearcher($"associators of {{Win32_DiskDrive.DeviceID='{drive["DeviceID"]}'}} " +
                                                         "where AssocClass = Win32_DiskDriveToDiskPartition").Get();

                        foreach (ManagementObject partition in partitionCollection) {
                            if (partition != null) {
                                // associate partitions with logical disks (drive letter volumes)
                                ManagementObjectCollection logicalCollection =
                                    new
                                        ManagementObjectSearcher(String
                                                                     .Format("associators of {{Win32_DiskPartition.DeviceID='{0}'}} " + "where AssocClass= Win32_LogicalDiskToPartition",
                                                                             partition["DeviceID"])).Get();

                                foreach (ManagementObject logical in logicalCollection) {
                                    if (logical != null) {
                                        // finally find the logical disk entry
                                        ManagementObjectCollection.ManagementObjectEnumerator volumeEnumerator =
                                            new ManagementObjectSearcher("select DeviceID from Win32_LogicalDisk " + $"where Name='{logical["Name"]}'")
                                                .Get().GetEnumerator();
                                        volumeEnumerator.MoveNext();
                                        ManagementObject volume = (ManagementObject) volumeEnumerator.Current;

                                        if (drive_letter.ToLowerInvariant().Equals(volume["DeviceID"].ToString().ToLowerInvariant()) &&
                                            (drive["MediaType"].ToString().ToLowerInvariant().Contains("external") ||
                                             drive["InterfaceType"].ToString().ToLowerInvariant().Contains("usb"))) {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                } catch {
                    Thread.Sleep(100);
                }

        return false;
    }


        public IEnumerable<IFolder> folders {
            get { return new DirectoryInfo(root_).EnumerateDirectories().Select(f => new win_folder(root_, f.Name)); }
        }
        public IEnumerable<IFile> files {
            get { return new DirectoryInfo(root_).EnumerateFiles().Select(f => new win_file(root_, f.Name)); }
        }

        public IFile parse_file(string path) {
            var f = try_parse_file(path);
            if ( f == null)
                throw new external_drive_libexception("invalid path " + path);
            return f;
        }

        public IFolder parse_folder(string path) {
            var f = try_parse_folder(path);
            if ( f == null)
                throw new external_drive_libexception("invalid path " + path);
            return f;
        }

        public IFile try_parse_file(string path) {
            path = path.Replace("/", "\\");
            var contains_drive_prefix = path.StartsWith(root_, StringComparison.CurrentCultureIgnoreCase);
            var full = contains_drive_prefix ? path : root_ + path;
            if (File.Exists(full)) {
                var fi = new FileInfo(full);
                return new win_file(fi.DirectoryName, fi.Name);
            }
            return null;
        }

        public IFolder try_parse_folder(string path) {
            path = path.Replace("/", "\\");
            var contains_drive_prefix = path.StartsWith(root_, StringComparison.CurrentCultureIgnoreCase);
            var full = contains_drive_prefix ? path : root_ + path;
            if (Directory.Exists(full)) {
                var fi = new DirectoryInfo(full);
                return new win_folder(fi.Parent.FullName, fi.Name);
            }
            return null;
        }

        public IFolder create_folder(string path) {
            path = path.Replace("/", "\\");
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);
            var contains_drive_prefix = path.StartsWith(root_, StringComparison.CurrentCultureIgnoreCase);
            if (!contains_drive_prefix)
                path = root_ + path;
            Directory.CreateDirectory(path);

            return parse_folder(path);
        }
    }
}
