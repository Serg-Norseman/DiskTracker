﻿/*
 *  "FileChecker".
 *  Copyright (C) 2018 by Sergey V. Zhdanovskih.
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using EXControls;

namespace FileChecker
{
    public partial class MainForm : Form, IUserForm
    {
        private readonly SynchronizationContext fSyncContext;
        private readonly int fProcessorCores;
        private bool[] fCoreBusy;

        private string folderName;

        private List<string> fFiles;
        private int fLastIndex;
        private int fCompleted;

        EXListViewItem[] fListItems;
        ProgressBar[] fProgressBars;

        public MainForm()
        {
            InitializeComponent();
            fSyncContext = SynchronizationContext.Current;

            fProcessorCores = FCCore.GetProcessorsCount();
            fCoreBusy = new bool[fProcessorCores];
            Array.Clear(fCoreBusy, 0, fProcessorCores);

            Text = "ProcessorsCount: " + fProcessorCores;

            exListView1.Columns.Add("File", 300);
            exListView1.Columns.Add("Progress", 200);

            fListItems = new EXListViewItem[fProcessorCores];
            fProgressBars = new ProgressBar[fProcessorCores];
            for (int i = 0; i < fProcessorCores; i++) {
                EXListViewItem item = new EXListViewItem("Item " + i);
                EXControlListViewSubItem cs = new EXControlListViewSubItem();
                ProgressBar b = new ProgressBar();
                b.Minimum = 0;
                b.Maximum = 100;
                b.Step = 1;
                item.SubItems.Add(cs);
                exListView1.AddControlToSubItem(b, cs);
                exListView1.Items.Add(item);

                fListItems[i] = item;
                fProgressBars[i] = b;
            }
        }

        private void btnProcess_Click(object sender, EventArgs e)
        {
            using (var fldDlg = new FolderBrowserDialog()) {
                fldDlg.SelectedPath = folderName;
                if (fldDlg.ShowDialog() == DialogResult.OK) {
                    folderName = fldDlg.SelectedPath;
                    Process();
                }
            }
        }

        private readonly ManualResetEvent initEvent = new ManualResetEvent(false);
        private bool fEmptyList;

        private void Process()
        {
            fFiles = WalkDirectoryTree(new DirectoryInfo(folderName), true);
            fLastIndex = -1;
            fEmptyList = false;
            initEvent.Set();
            ProgressBar.Value = 0;
            ProgressBar.Maximum = fFiles.Count;
            ProgressBar.Visible = true;
            fCompleted = 0;

            new Thread(() => {
                while (!fEmptyList) {
                    if (initEvent.WaitOne()) {
                        lock (fFiles) {
                            lock (fCoreBusy) {
                                int freeCore = GetFreeCore();
                                while (freeCore >= 0) {
                                    if (fLastIndex >= fFiles.Count - 1) {
                                        fEmptyList = true;
                                        break;
                                    }
                                    fLastIndex += 1;
                                    string fileName = fFiles[fLastIndex];

                                    fCoreBusy[freeCore] = true;
                                    CreateFileHashThread(fLastIndex, freeCore, fileName);

                                    freeCore = GetFreeCore();
                                }
                            }
                        }

                        initEvent.Reset();
                    }
                }
            }).Start();
        }

        private List<string> WalkDirectoryTree(DirectoryInfo root, bool showHidden)
        {
            var result = new List<string>();

            UpdateProgress(0, "Folder scanning");

            var dirStack = new Stack<DirectoryInfo>();
            dirStack.Push(root);

            while (dirStack.Count > 0) {
                DirectoryInfo currentDir = dirStack.Pop();

                if (!FCCore.CheckAttributes(currentDir.Attributes, showHidden)) {
                    continue;
                }

                try {
                    FileInfo[] files = currentDir.GetFiles("*.*");
                    foreach (FileInfo file in files) {
                        try {
                            if (!FCCore.CheckAttributes(file.Attributes, showHidden)) {
                                continue;
                            }

                            result.Add(file.FullName);

                            UpdateProgress(1, file.FullName);
                        } catch (FileNotFoundException) {
                        }
                    }
                } catch (UnauthorizedAccessException) {
                } catch (DirectoryNotFoundException) {
                }

                try {
                    DirectoryInfo[] subDirs = currentDir.GetDirectories();
                    foreach (DirectoryInfo dir in subDirs) {
                        dirStack.Push(dir);
                    }
                } catch (UnauthorizedAccessException) {
                } catch (DirectoryNotFoundException) {
                }
            }

            UpdateProgress(2, "");

            return result;
        }

        private void UpdateProgress(int action, string value)
        {
            switch (action) {
                case 0:
                case 2:
                    StatusText.Text = value;
                    //tsProgress.Maximum = 100;
                    //tsProgress.Value = 0;
                    break;

                case 1:
                    StatusText.Text = value;
                    /*if (value >= tsProgress.Minimum && value <= tsProgress.Maximum) {
                        tsProgress.Value = value;
                    }*/
                    break;
            }
        }

        private int GetFreeCore()
        {
            for (int i = 0; i < fCoreBusy.Length; i++) {
                if (!fCoreBusy[i]) {
                    return i;
                }
            }
            return -1;
        }

        #region Thread's reporting functions

        void IUserForm.ReportHash(ThreadFileObj fileObj)
        {
            fSyncContext.Post(UpdateHash, fileObj);

            lock (fCoreBusy) {
                int threadCore = fileObj.Core;
                fCoreBusy[threadCore] = false;
            }

            initEvent.Set();

            var core = FCCore.CORES[fileObj.Core];
            fSyncContext.Post(UpdateLog, core.ToString() + ", FileHash (" + fileObj.FileName + "): " + FCCore.Hash2Str(fileObj.Hash));
        }

        private void UpdateHash(object state)
        {
            ThreadFileObj fileObj = (ThreadFileObj)state;
            //fileObj.Hash
            fProgressBars[fileObj.Core].Value = 0;
            fListItems[fileObj.Core].Text = "?";
            exListView1.Update();

            fCompleted += 1;
            ProgressBar.Value = fCompleted;
        }

        void IUserForm.ReportProgress(ThreadFileObj fileObj)
        {
            fSyncContext.Post(UpdateProgress, fileObj);
        }

        private void UpdateProgress(object state)
        {
            ThreadFileObj fileObj = (ThreadFileObj)state;
            fProgressBars[fileObj.Core].Value = fileObj.Progress;
        }

        void IUserForm.ReportStart(ThreadFileObj fileObj)
        {
            fSyncContext.Post(UpdateStart, fileObj);
        }

        private void UpdateStart(object state)
        {
            ThreadFileObj fileObj = (ThreadFileObj)state;
            fListItems[fileObj.Core].Text = fileObj.FileName;
            exListView1.Update();
        }

        void IUserForm.ReportLog(string msg)
        {
            fSyncContext.Post(UpdateLog, msg);
        }

        private void UpdateLog(object state)
        {
            string msg = (string)state;
            textBox2.AppendText(msg + "\r\n");
        }

        #endregion

        #region Calculate file's hashes

        private void CreateFileHashThread(int index, int coreNum, string fileName)
        {
            ProcessorCore core = FCCore.CORES[coreNum];
            ((IUserForm)this).ReportLog(core.ToString() + ", Processing: " + fileName);

            var fileObj = new ThreadFileObj(index, coreNum, fileName, this);
            ((IUserForm)this).ReportStart(fileObj);

            DistributedThread thread = new DistributedThread(ParameterizedThreadProc);
            thread.ProcessorAffinity = (int)core;
            thread.Start(fileObj);
        }

        private static void ParameterizedThreadProc(object obj)
        {
            var fileObj = ((ThreadFileObj)obj);
            FCCore.CalculateFileHash(fileObj);
        }

        #endregion
    }
}
