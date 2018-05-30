﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using external_drive_lib.exceptions;
using external_drive_lib.interfaces;
using external_drive_lib.monitor;
using external_drive_lib.portable;
using external_drive_lib.util;
using external_drive_lib.windows;
using Shell32;

namespace external_drive_lib
{
    /* the root - the one that contains all external drives 
     */
    public class drive_root {

        public static drive_root inst { get; } = new drive_root();

        private bool auto_close_win_dialogs_ = true;

        // note: not all devices register as USB hubs, some only register as controller devices
        private monitor_devices monitor_usbhub_devices_ = new monitor_devices ();
        private monitor_devices monitor_controller_devices_ = new monitor_devices ();
        
        private monitor_usb_drives monitor_usb_drives_ = new monitor_usb_drives();
        private Dictionary<string,string> vidpid_to_unique_id_ = new Dictionary<string, string>();

        private const string INVALID_UNIQUE_ID = "_invalid_";

        private List<char> external_drives_ = new List<char>();

        private drive_root() {
            // not really proud of swallowing exceptions here, but otherwise if we were not able to create the drive_root object,
            // any other function would likely end up throwing
            try {
                var existing_devices = find_devices.find_objects("Win32_USBHub");
                foreach (var device in existing_devices) {
                    if (device.ContainsKey("PNPDeviceID")) {
                        var device_id = device["PNPDeviceID"];
                        string vid_pid = "", unique_id = "";
                        if (usb_util.pnp_device_id_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id))
                            add_vidpid(vid_pid, unique_id);
                    }
                }
            } catch {
            }

            try {
                var existing_controller_devices = find_devices.find_objects("Win32_USBControllerDevice");
                foreach (var device in existing_controller_devices) {
                    if (device.ContainsKey("Dependent")) {
                        var device_id = device["Dependent"];
                        string vid_pid = "", unique_id = "";
                        if (usb_util.dependent_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id))
                            add_vidpid(vid_pid, unique_id);
                    }
                }
            } catch {
            }

            try {
                refresh();
            } catch {
            }

            try {
                monitor_usbhub_devices_.added_device += device_added;
                monitor_usbhub_devices_.deleted_device += device_removed;
                monitor_usbhub_devices_.monitor("Win32_USBHub");
            } catch {                
            }

            try {
                monitor_controller_devices_.added_device += device_added_controller;
                monitor_controller_devices_.deleted_device += device_removed_controller;
                monitor_controller_devices_.monitor("Win32_USBControllerDevice");
            } catch {                
            }

            new Thread(win32_util.check_for_dialogs_thread) {IsBackground = true}.Start();
        }

        private void add_vidpid(string vid_pid, string unique_id) {
            lock (this)
                if (!vidpid_to_unique_id_.ContainsKey(vid_pid))
                    vidpid_to_unique_id_.Add(vid_pid, unique_id);
            // if the same unique ID, we're fine
                else if ( vidpid_to_unique_id_[vid_pid] != unique_id)
                    // two vid-pids with the same ID - we ignore them altogether
                    vidpid_to_unique_id_[vid_pid] = INVALID_UNIQUE_ID;            
        }

        // returns all drives, even the internal HDDs - you might need this if you want to copy a file onto an external drive
        public IReadOnlyList<IDrive> drives {
            get { lock (this) return drives_; }
        }



        private void on_new_device(string vid_pid, string unique_id) {
            add_vidpid(vid_pid, unique_id);
            lock(this)
                if (vidpid_to_unique_id_[vid_pid] == INVALID_UNIQUE_ID)
                    return; // duplicate vidpids

            refresh_portable_unique_ids();
            var already_a_drive = false;
            lock (this) {
                var ad = drives_.FirstOrDefault(d => d.unique_id == unique_id) as portable_drive;
                if (ad != null) {
                    ad.connected_via_usb = true;
                    already_a_drive = true;
                }
            }
            if (!already_a_drive)
                win_util.postpone(() => monitor_for_drive(vid_pid, 0), 50);            
        }

        private void on_deleted_device(string vid_pid, string unique_id) {            
            lock (this) {
                var ad = drives_.FirstOrDefault(d => d.unique_id == unique_id) as portable_drive;
                if (ad != null)
                    ad.connected_via_usb = false;
            }
            refresh();
        }

        private void device_added_controller(Dictionary<string, string> properties) {
            if (properties.ContainsKey("Dependent")) {
                var device_id = properties["Dependent"];
                string vid_pid = "", unique_id = "";
                if (usb_util.dependent_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id)) 
                    on_new_device(vid_pid, unique_id);
            } 
        }
        private void device_removed_controller(Dictionary<string, string> properties) {
            if (properties.ContainsKey("Dependent")) {
                var device_id = properties["Dependent"];
                string vid_pid = "", unique_id = "";
                if (usb_util.dependent_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id)) 
                    on_deleted_device(vid_pid, unique_id);
            } 
        }

        private void device_added(Dictionary<string, string> properties) {
            if (properties.ContainsKey("PNPDeviceID")) {
                var device_id = properties["PNPDeviceID"];
                string vid_pid = "", unique_id = "";
                if (usb_util.pnp_device_id_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id)) 
                    on_new_device(vid_pid, unique_id);
            } else {
                // added usb device with no PNPDeviceID
                Debug.Assert(false);
            }
        }

        // here, we know the drive was connected, wait a bit until it's actually visible
        private void monitor_for_drive(string vidpid, int idx) {
            const int MAX_RETRIES = 10;
            var drives_now = get_portable_drives();
            var found = drives_now.FirstOrDefault(d => (d as portable_drive).vid_pid == vidpid);
            if (found != null) 
                refresh();
            else if (idx < MAX_RETRIES)
                win_util.postpone(() => monitor_for_drive(vidpid, idx + 1), 100);
            else {
                // "can't find usb connected drive " + vidpid
                Debug.Assert(false);
            }
        }

        private void device_removed(Dictionary<string, string> properties) {
            if (properties.ContainsKey("PNPDeviceID")) {
                var device_id = properties["PNPDeviceID"];
                string vid_pid = "", unique_id = "";
                if (usb_util.pnp_device_id_to_vidpid_and_unique_id(device_id, ref vid_pid, ref unique_id)) 
                    on_deleted_device(vid_pid, unique_id);                
            } else {
                // deleted usb device with no PNPDeviceID
                Debug.Assert(false);
            }
        }


        public bool auto_close_win_dialogs {
            get { return auto_close_win_dialogs_; }
            set {
                if (auto_close_win_dialogs_ == value)
                    return;
                auto_close_win_dialogs_ = value;
            }
        }

        // this includes all drives, even the internal ones
        private List<IDrive> drives_ = new List<IDrive>();

        public void refresh() {
            List<IDrive> drives_now = new List<IDrive>();
            try {
                drives_now.AddRange(get_win_drives());
                bool different;
                lock (this) {
                    var old_win = drives_.Where(d => !d.type.is_portable()).Select(d => d.unique_id).ToList();
                    var new_win = drives_now.Select(d => d.unique_id).ToList();
                    different = !old_win.SequenceEqual(new_win);
                }
                if (different) {
                    // we have a different number of windows drives ->recompute the external drives (this is CPU intensive, and we want to avoid calling it too many times)
                    var ed = win_util.external_disk_drives();
                    lock (this)
                        external_drives_ = ed;
                }

                if (different) {
                    drives_now.Clear();
                    drives_now.AddRange(get_win_drives());
                }

            } catch (Exception e) {
                throw new external_drive_libexception( "error getting win drives ", e);
            }
            try {
                drives_now.AddRange(get_portable_drives());
            } catch (Exception e) {
                throw new external_drive_libexception("error getting android drives ", e);
            }
            lock (this) {
                drives_ = drives_now;
            }
            refresh_portable_unique_ids();
        }

        private void refresh_portable_unique_ids() {
            lock (this)
                foreach ( portable_drive ad in drives_.OfType<portable_drive>())
                    if ( vidpid_to_unique_id_.ContainsKey(ad.vid_pid) && vidpid_to_unique_id_[ad.vid_pid] != INVALID_UNIQUE_ID)
                        ad.unique_id = vidpid_to_unique_id_[ad.vid_pid];
        }

        // As drive name, use any of: 
        // "{<unique_id>}:", "<drive-name>:", "[a<android-drive-index>]:", "[i<ios-index>]:", "[p<portable-index>]:", "[d<drive-index>]:"
        public IDrive try_get_drive(string drive_prefix) {
            drive_prefix = drive_prefix.Replace("/", "\\");
            // case insensitive
            foreach ( var d in drives)
                if (string.Compare(d.root_name, drive_prefix, StringComparison.CurrentCultureIgnoreCase) == 0 ||
                    string.Compare("{" + d.unique_id + "}:\\", drive_prefix, StringComparison.CurrentCultureIgnoreCase) == 0 ||
                    string.Compare(d.unique_id , drive_prefix, StringComparison.CurrentCultureIgnoreCase) == 0)
                    return d;

            if (drive_prefix.StartsWith("[") && drive_prefix.EndsWith("]:\\")) {
                drive_prefix = drive_prefix.Substring(1, drive_prefix.Length - 4);
                if (drive_prefix.StartsWith("d", StringComparison.CurrentCultureIgnoreCase)) {
                    // d<drive-index>
                    drive_prefix = drive_prefix.Substring(1);
                    var idx = 0;
                    if (int.TryParse(drive_prefix, out idx)) {
                        var all = drives;
                        if (all.Count > idx)
                            return all[idx];
                    }
                }
                else if (drive_prefix.StartsWith("a", StringComparison.CurrentCultureIgnoreCase)) {
                    drive_prefix = drive_prefix.Substring(1);
                    var idx = 0;
                    if (int.TryParse(drive_prefix, out idx)) {
                        var android = drives.Where(d => d.type.is_android()).ToList();
                        if (android.Count > idx)
                            return android[idx];
                    }                    
                }
                else if (drive_prefix.StartsWith("i", StringComparison.CurrentCultureIgnoreCase)) {
                    drive_prefix = drive_prefix.Substring(1);
                    var idx = 0;
                    if (int.TryParse(drive_prefix, out idx)) {
                        var ios = drives.Where(d => d.type.is_iOS()).ToList();
                        if (ios.Count > idx)
                            return ios[idx];
                    }                    
                }
                else if (drive_prefix.StartsWith("p", StringComparison.CurrentCultureIgnoreCase)) {
                    drive_prefix = drive_prefix.Substring(1);
                    var idx = 0;
                    if (int.TryParse(drive_prefix, out idx)) {
                        var portable = drives.Where(d => d.type.is_portable()).ToList();
                        if (portable.Count > idx)
                            return portable[idx];
                    }                    
                }
            }

            return null;
        }
        // throws if drive not found
        public IDrive get_drive(string drive_prefix) {
            // case insensitive
            var d = try_get_drive(drive_prefix);
            if ( d == null)
                throw new external_drive_libexception("invalid drive " + drive_prefix);
            return d;
        }

        internal string try_get_unique_id_for_drive(char letter) {
            return monitor_usb_drives_.unique_id(letter);
        }

        private void split_into_drive_and_folder_path(string path, out string drive, out string folder_or_file) {
            path = path.Replace("/", "\\");
            var end_of_drive = path.IndexOf(":\\");
            if (end_of_drive >= 0) {
                drive = path.Substring(0, end_of_drive + 2);
                folder_or_file = path.Substring(end_of_drive + 2);
            } else
                drive = folder_or_file = null;
        }

        // returns null on failure
        public IFile try_parse_file(string path) {
            // split into drive + path
            string drive_str, folder_or_file;
            split_into_drive_and_folder_path(path, out drive_str, out folder_or_file);
            if (drive_str == null)
                return null;
            var drive = get_drive(drive_str);
            return drive.try_parse_file(folder_or_file);            
        }

        // returns null on failure
        public IFolder try_parse_folder(string path) {
            string drive_str, folder_or_file;
            split_into_drive_and_folder_path(path, out drive_str, out folder_or_file);
            if ( drive_str == null)
                return null;
            var drive = try_get_drive(drive_str);
            if (drive == null)
                return null;
            return drive.try_parse_folder(folder_or_file);            
        }

        // throws if anything goes wrong
        public IFile parse_file(string path) {
            // split into drive + path
            string drive_str, folder_or_file;
            split_into_drive_and_folder_path(path, out drive_str, out folder_or_file);
            if ( drive_str == null)
                throw new external_drive_libexception("invalid path " + path);
            var drive = try_get_drive(drive_str);
            if (drive == null)
                return null;
            return drive.parse_file(folder_or_file);
        }

        // throws if anything goes wrong
        public IFolder parse_folder(string path) {
            string drive_str, folder_or_file;
            split_into_drive_and_folder_path(path, out drive_str, out folder_or_file);
            if ( drive_str == null)
                throw new external_drive_libexception("invalid path " + path);
            var drive = get_drive(drive_str);
            return drive.parse_folder(folder_or_file);
        }

        // creates all folders up to the given path
        public IFolder new_folder(string path) {
            string drive_str, folder_or_file;
            split_into_drive_and_folder_path(path, out drive_str, out folder_or_file);
            if ( drive_str == null)
                throw new external_drive_libexception("invalid path " + path);
            var drive = get_drive(drive_str);
            return drive.create_folder(folder_or_file);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Portable


        private List<IDrive> get_portable_drives() {
            var new_drives = portable_util. get_portable_connected_device_drives().Select(d => new portable_drive(d) as IDrive).ToList();
            List<IDrive> old_drives = null;
            lock (this)
                old_drives = drives_.Where(d => d is portable_drive).ToList();

            // if we already have this drive, reuse that
            List<IDrive> result = new List<IDrive>();
            foreach (var new_ in new_drives) {
                var old = old_drives.FirstOrDefault(od => od.root_name == new_.root_name);
                result.Add(old ?? new_);
            }
            return result;
        }

        // END OF Portable
        //////////////////////////////////////////////////////////////////////////////////////////////////////////

        //////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Windows

        // for now, I return all drives - don't care about which is External, Removable, whatever

        private List<IDrive> get_win_drives() {
            List<char> external_drives;
            lock (this)
                external_drives = external_drives_;
            return DriveInfo.GetDrives().Select(d => new win_drive(d,external_drives) as IDrive).ToList();
        }
        // END OF Windows
        //////////////////////////////////////////////////////////////////////////////////////////////////////////

    }
}
