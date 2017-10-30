﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using external_drive_lib;
using external_drive_lib.interfaces;

namespace console_test
{
    class Program
    {
        static void dump_folders_and_files(IEnumerable<IFolder> folders, IEnumerable<IFile> files, int indent) {
            Console.WriteLine("");
            Console.WriteLine("Level " + (indent+1));
            Console.WriteLine("");
            foreach ( var f in folders)
                Console.WriteLine(new string(' ', indent * 2) + f.full_path + " " + f.name);
            foreach ( var f in files)
                Console.WriteLine(new string(' ', indent * 2) + f.full_path + " " + f.name + " " + f.size + " " + f.last_write_time);
        } 

        static void traverse_drive(IDrive d, int levels) {
            var folders = d.folders.ToList();
            // level 1
            dump_folders_and_files(folders, d.files, 0);
            for (int i = 1; i < levels; ++i) {
                var child_folders = new List<IFolder>();
                var child_files = new List<IFile>();
                foreach (var f in folders) {
                    try {
                        child_folders.AddRange(f.child_folders);
                    } catch(Exception e) {
                        // could be unauthorized access
                        Console.WriteLine(new string(' ', i * 2) + f.full_path + " *** UNAUTHORIZED folders " + e);
                    }
                    try {
                        child_files.AddRange(f.files);
                    } catch {
                        // could be unauthorized access
                        Console.WriteLine(new string(' ', i * 2) + f.full_path + " *** UNAUTHORIZED files");
                    }
                }
                dump_folders_and_files(child_folders, child_files, i);
                folders = child_folders;
            }
        }

        // these are files from my drive
        static void test_win_parse_files() {
            Debug.Assert(drive_root.inst.parse_file("D:\\cool_pics\\a00\\b0\\c0\\20161115_035718.jPg").size == 4532595);
            Debug.Assert(drive_root.inst.parse_file("D:\\cool_pics\\a00\\b0\\c0\\20161115_104952.jPg").size == 7389360);
            Debug.Assert(drive_root.inst.parse_folder("D:\\cool_pics\\a10").files.Count() == 25);
            Debug.Assert(drive_root.inst.parse_folder("D:\\cool_pics").child_folders.Count() == 8);

            Debug.Assert(drive_root.inst.parse_file("D:\\cool_pics\\a00\\b0\\c0\\20161115_035718.jPg").full_path == "D:\\cool_pics\\a00\\b0\\c0\\20161115_035718.jPg");
            Debug.Assert(drive_root.inst.parse_folder("D:\\cool_pics\\a10").full_path == "D:\\cool_pics\\a10");
        }

        static void test_parent_folder() {
            Debug.Assert(drive_root.inst.parse_file("D:\\cool_pics\\a00\\b0\\c0\\20161115_035718.jPg").folder.full_path == "D:\\cool_pics\\a00\\b0\\c0");
            Debug.Assert(drive_root.inst.parse_file("D:\\cool_pics\\a00\\b0\\c0\\20161115_035718.jPg").folder.parent.full_path == "D:\\cool_pics\\a00\\b0");
        }

        // copies all files from this folder into a sub-folder we create
        // after we do that, we delete the sub-folder
        static void test_copy_and_delete_files(string src_path) {
            var src = drive_root.inst.parse_folder(src_path);
            var old_folder_count = src.child_folders.Count();
            var child_dir = src_path + "/child1/child2/child3/";
            var dest = src.drive.create_folder(child_dir);
            foreach ( var child in src.files)
                child.copy(child_dir);
            long src_size = src.files.Sum(f => f.size);
            long dest_size = dest.files.Sum(f => f.size);
            Debug.Assert(src_size == dest_size);
            Debug.Assert(src.child_folders.Count() == old_folder_count + 1);
            foreach (var child in dest.files)
                child.delete();

            var first_child = dest.parent.parent;
            first_child.delete();

            Debug.Assert(src.child_folders.Count() == old_folder_count );
        }

        static void Main(string[] args)
        {
            //traverse_drive( drive_root.inst.get_drive("d:\\"), 3);
            //test_win_parse_files();
            //test_parent_folder();
            //test_copy_and_delete_files("D:\\cool_pics\\a00\\b0\\c0\\");
            traverse_drive( drive_root.inst.get_drive("{galaxy s6}"), 4);
        }
    }
}
