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
using external_drive_lib.util;
using Shell32;

namespace external_drive_lib.windows
{
    internal static class win_util {
        private static readonly string temporary_root_dir_ = temporary_root_dir_impl();
        private static string temporary_root_dir_impl() {
            // we need a unique folder each time we're run, so that we never run into conflicts when moving stuff here
            var root_dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\";
            if (Directory.Exists(root_dir + "Temp"))
                root_dir += "Temp\\";
            root_dir += "external_drive_temp\\";
            var dir = root_dir + DateTime.Now.Ticks;
            // FIXME create a task to erase all other folders (previously created that is)
            try {
                // erase all the other created folders, if any
                Directory.CreateDirectory(root_dir);
                var prev_dirs = new DirectoryInfo(root_dir).EnumerateDirectories().Select(d => d.FullName).ToList();
                Directory.CreateDirectory(dir);
                Task.Run(() => delete_folders(prev_dirs));                
            } catch {
            }
            return dir;
        }

        private static void delete_folders(List<string> folders) {
            foreach ( var f in folders)
                try {
                    Directory.Delete(f, true);
                } catch {
                }
        }

        public static string temporary_root_dir() {
            return temporary_root_dir_;
        }

        public static Folder get_shell32_folder(object folder_path)
        {
            Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
            Object shell = Activator.CreateInstance(shellAppType);
            return (Folder)shellAppType.InvokeMember("NameSpace",
                                                     System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { folder_path });
        }

        private static long wait_for_win_file_size(string file_name, long size, int retry_count) {
            // 1.2.4+ note - that 0 can be a valid file size, meaning we're in the process of copying the file
            long cur_size = 0;
            for (int i = 0; i < retry_count && cur_size < size; ++i) {
                //if ( File.Exists(file_name))
                    try {
                        cur_size = win32_util.file_len(file_name);
                    } catch {
                    }
                if ( cur_size < size)
                    Thread.Sleep(50);
            }
            return cur_size;
        }
        public static void wait_for_win_copy_complete(long size, string file_name, int max_retry = 25, int max_retry_first_time = 100) {
            // 1.2.4+ 0 can be a valid size, that can mean we started copying
            long last_size = 0;
            // the idea is - if after waiting a while, something got copied (size has changed), we keep waiting
            // the copy process can take a while to start...
            last_size = wait_for_win_file_size(file_name, size, max_retry_first_time);
            if ( last_size <= 0)
                throw new external_drive_libexception("File may have not been copied - " + file_name + " got 0, expected " + size);

            // the idea is - if after waiting a while, something got copied (size has changed), we keep waiting
            while (last_size < size) {
                var cur_size = wait_for_win_file_size(file_name, size, max_retry);
                if ( cur_size == last_size)
                    throw new external_drive_libexception("File may have not been copied - " + file_name + " got " + cur_size + ", expected " + size);
                last_size = cur_size;
            }
        }

        private static long wait_for_android_file_size(string full_file_name, long size, int retry_count) {
            long cur_size = -1;
            for (int i = 0; i < retry_count ; ++i) {
                try {
                    cur_size = drive_root.inst.parse_file(full_file_name).size;
                } catch {
                }
                if ( cur_size < size)
                    Thread.Sleep(50);
            }
            return cur_size;
        }
        public static void wait_for_portable_copy_complete(string full_file_name, long size, int max_retry = 25, int max_retry_first_time = 100) {
            long last_size = -1;
            // the idea is - if after waiting a while, something got copied (size has changed), we keep waiting
            // the copy process can take a while to start...
            last_size = wait_for_android_file_size(full_file_name, size, max_retry_first_time);
            if ( last_size < 0)
                throw new external_drive_libexception("File may have not been copied - " + full_file_name + " got -1, expected " + size);
            while (last_size < size) {
                var cur_size = wait_for_android_file_size(full_file_name, size, max_retry);
                if ( cur_size == last_size)
                    throw new external_drive_libexception("File may have not been copied - " + full_file_name + " got " + cur_size + ", expected " + size);
                last_size = cur_size;
            }
        }



        // note: this can only happen synchronously - otherwise, we'd end up deleting something from HDD before it was fully moved from the HDD
        public static void delete_sync_portable_file(FolderItem2 fi) {
            Debug.Assert( !fi.IsFolder);
            // https://msdn.microsoft.com/en-us/library/windows/desktop/bb787874(v=vs.85).aspx
            var move_options = 4 | 16 | 512 | 1024;
            try {
                var temp = win_util.temporary_root_dir();
                var temp_folder = win_util.get_shell32_folder(temp);
                var file_name = fi.Name;
                var size = portable_util.portable_file_size(fi);
                temp_folder.MoveHere(fi, move_options);

                var name = temp + "\\" + file_name;
                wait_for_win_copy_complete(size, temp + "\\" + file_name);
                File.Delete(name);
            } catch {
                // backup - this will prompt a confirmation dialog
                // googled it quite a bit - there's no way to disable it
                fi.InvokeVerb("delete");
            }            
        }

        private static void get_recursive_size(DirectoryInfo dir, ref long size) {
            foreach ( var child in dir.EnumerateDirectories())
                get_recursive_size(child, ref size);

            foreach (var f in dir.EnumerateFiles())
                size += f.Length;
        }
        // FIXME surround in try/catch?
        public static void wait_for_win_folder_move_complete(string folder, string old_full_path) {
            long last_size = -1;
            const int retry_find_folder_move_complete = 20;
            for (int r = 0; r < retry_find_folder_move_complete; ++r) {
                const int retry_get_recursive_size_count = 4;
                long cur_size = 0;
                get_recursive_size(new DirectoryInfo(folder), ref cur_size);
                for (var i = 0; i < retry_get_recursive_size_count && cur_size == last_size; ++i) {
                    Thread.Sleep(100);
                    cur_size = 0;
                    get_recursive_size(new DirectoryInfo(folder), ref cur_size);
                }
                if (cur_size > last_size) {
                    // at this point, the size increased - therefore, it's still moving files
                    last_size = cur_size;
                    // ... we'll wait until for X times we find not folder change
                    r = 0;
                    continue;
                }
                // at this point, we know that for 'retry_count' consecutive tries, the size remained the same
                // therefore, lets find out if the original Folder still exists
                try {
                    // if this doesn't throw, the folder is still not fully moved
                    drive_root.inst.parse_folder(old_full_path);
                } catch {
                    return;
                }
            }
            // here, we're not really sure if the move worked - for retry_find_folder_move_complete, the recursive size hasn't changed,
            // and we old folder still exists
            throw new external_drive_libexception("could not delete " + old_full_path);
        }


        // note: this can only happen synchronously - otherwise, we'd end up deleting something from HDD before it was fully moved from the HDD
        public static void delete_sync_portable_folder(FolderItem fi, string old_full_path) {
            Debug.Assert( fi.IsFolder);

            // https://msdn.microsoft.com/en-us/library/windows/desktop/bb787874(v=vs.85).aspx
            var move_options = 4 | 16 | 512 | 1024;
            try {
                var temp = win_util.temporary_root_dir();
                var temp_folder = win_util.get_shell32_folder(temp);
                var folder_name = fi.Name;
                temp_folder.MoveHere(fi, move_options);

                // wait until folder dissapears from Android (the alternative would be to check all the file sizes match the original file sizes.
                // however, we'd need to do this recursively)

                var name = temp + "\\" + folder_name;
                wait_for_win_folder_move_complete(name, old_full_path);
                Directory.Delete(name, true);
            } catch {
                // backup - this will prompt a confirmation dialog
                // googled it quite a bit - there's no way to disable it
                fi.InvokeVerb("delete");
            }
        }

        public static void postpone(Action a, int ms) {
            // not the best work, but for now it works
            Task.Run(() => {
                Thread.Sleep(ms);
                a();
            });
        }


        //https://stackoverflow.com/questions/9891854/how-to-determine-if-drive-is-external-drive
        //
        // 1.4.7+ note: this is insanely high-CPU intensive, thus we want to mimize the calls to this function
        public static List<char> external_disk_drives() {

            List<char> external_drives = new List<char>();

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

                                        var volume_id = volume["DeviceID"].ToString().ToLowerInvariant();
                                        var is_drive = volume_id.Length == 2 && volume_id[1] == ':' && Char.IsLetter(volume_id[0]);
                                        if (is_drive) {
                                            var media = drive["MediaType"];
                                            var interface_type = drive["InterfaceType"];
                                            var is_external = false;
                                            if (media?.ToString().ToLowerInvariant().Contains("external") ?? false)
                                                is_external = true;
                                            if (interface_type?.ToString().ToLowerInvariant().Contains("usb") ?? false)
                                                is_external = true;
                                            if ( is_external)
                                                external_drives.Add(volume_id[0]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                } catch {
                    Thread.Sleep(100);
                    external_drives.Clear();
                }

            return external_drives;
        }

    }
}
