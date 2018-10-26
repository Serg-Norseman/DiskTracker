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

namespace FileChecker
{
    public interface IUserForm
    {
        void ReportLog(string msg);
        void ReportProgress(ThreadFileObj fileObj);
        void ReportHash(ThreadFileObj fileObj);
        void ReportStart(ThreadFileObj fileObj);
    }

    public sealed class ThreadFileObj
    {
        public readonly int Index;
        public readonly string FileName;
        public readonly IUserForm UserForm;

        public int Core;
        public int Progress;
        public byte[] Hash;

        public ThreadFileObj(int index, int core, string fileName, IUserForm userForm)
        {
            Index = index;
            Core = core;
            FileName = fileName;
            UserForm = userForm;
        }
    }
}
