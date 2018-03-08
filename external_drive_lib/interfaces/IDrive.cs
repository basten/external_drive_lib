﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace external_drive_lib.interfaces
{
    public enum drive_type {
        portable,
        // if this, we're not sure if it's phone or tablet or whatever
        android_unknown, 
        // it's an android phone
        android_phone, 
        // it's an android tablet
        android_tablet, 

        iOS_unknown,
        iphone,
        ipad,

        // SD Card
        sd_card, 

        // external hard drive
        external_hdd,

        // it's the Windows HDD 
        internal_hdd,

        // note: this is to be treated read-only!!!
        cd_rom,

        usb_stick,

        unknown,

        // mapped drive - could be very likely slow
        network,
    }

    public static class drive_type_os {
        public static bool is_android(this drive_type dt) {
            return dt == drive_type.android_unknown || dt == drive_type.android_phone || dt == drive_type.android_tablet;
        }

        public static bool is_portable(this drive_type dt) {
            return dt == drive_type.android_unknown || dt == drive_type.android_phone 
                || dt == drive_type.android_tablet || dt == drive_type.portable
                || dt == drive_type.iphone || dt == drive_type.ipad || dt == drive_type.iOS_unknown;
        }

        public static bool is_iOS(this drive_type dt) {
            return dt == drive_type.iphone || dt == drive_type.ipad || dt == drive_type.iOS_unknown;
        }
    };

    public interface IDrive {
        // returns true if the drive is connected
        // note: not as a property, since this could actually take time to find out - we don't want to break debugging
        bool is_connected();

        // returns true if the drive is available - note that the drive can be connected via USB, but locked (thus, not available)
        bool is_available();

        drive_type type { get; }

        // this is the drive path, such as "c:\" - however, for non-conventional drives, it can be a really weird path
        string root_name { get; }

        // the drive's Unique ID - it is the same between program runs
        string unique_id { get; }

        // a friendly name for the drive
        string friendly_name { get; }

        IEnumerable<IFolder> folders { get; }
        IEnumerable<IFile> files { get; }

        // throws on failure
        IFile parse_file(string path);
        IFolder parse_folder(string path);

        // returns null on failure
        IFile try_parse_file(string path);
        IFolder try_parse_folder(string path);

        // creates the full path to the folder
        IFolder create_folder(string folder);
    }

}
