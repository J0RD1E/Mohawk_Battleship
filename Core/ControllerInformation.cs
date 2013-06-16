﻿using MBC.Shared;
using MBC.Shared.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MBC.Core
{
    /// <summary>
    /// The Controller class manages the controller Type objects which are classes that implement the
    /// IBattleshipController interface. Statically, it handles loading these classes and provides the name
    /// and version of these classes through a list of generated controller classes that are used to
    /// determine the available controllers.<br/>
    /// Use the following methods in this order to load and retrieve information for controllers:
    /// <list type="number">
    ///     <item>LoadControllerFolder() or AddControllerFolder() - Populates the list of ClassInfo objects that contain the Type objects required to create controller interfaces.</item>
    ///     <item>GetControllerList() - Gets the list of ClassInfo objects that have been constructed.</item>
    ///     <item>GetControllersFromName() - Gets a list of ClassInfo objects from a name.</item>
    ///     <item>GetController() - Gets a ClassInfo object from a name and version number.</item>
    /// </list>
    /// Configuration keys:
    /// <list type="bullet">
    ///     <item><b>mbc_controller_thread_timeout</b> - The time to wait before aborting a called method in a controller interface.
    ///     This value must be greater than the mbc_match_timeout value.
    ///     </item>
    /// </list>
    /// </summary>
    /// <seealso cref="ClassInfo"/>
    public class ControllerInformation
    {
        private static List<ControllerInformation> loadedInformation;

        private NameAttribute nameAttrib;
        private VersionAttribute verAttrib;
        private CapabilitiesAttribute capableAttrib;
        private DescriptionAttribute descAttrib;
        private AuthorAttribute authorAttrib;
        private AcademicInfoAttribute academicAttrib;

        private string dllFile;
        private Type controllerInterface;

        /// <summary>
        /// Constructs a ClassInfo object with the given values.
        /// </summary>
        /// <param name="name">The name of the controller.</param>
        /// <param name="ver">The version number of the controller.</param>
        /// <param name="dll">The file name of the DLL that this controller was loaded in.</param>
        /// <param name="inter">The class implementing the IBattleshipInterface (un-constructed state).</param>
        public ControllerInformation(NameAttribute name, VersionAttribute ver, DescriptionAttribute desc,
            AuthorAttribute auth, AcademicInfoAttribute academic, CapabilitiesAttribute capabilities,
            string dll, Type inter)
        {
            this.nameAttrib = name;
            this.verAttrib = ver;
            this.descAttrib = desc;
            this.authorAttrib = auth;
            this.academicAttrib = academic;
            this.capableAttrib = capabilities;
            this.dllFile = dll;
            this.controllerInterface = inter;
        }

        public static List<ControllerInformation> AvailableControllers
        {
            get { return new List<ControllerInformation>(loadedInformation); }
        }

        /// <summary>
        /// Gets the name of this controller.
        /// </summary>
        public string Name
        {
            get
            {
                return nameAttrib.Name;
            }
        }

        /// <summary>
        /// Gets the version number of this controller.
        /// </summary>
        public Version Version
        {
            get
            {
                return verAttrib.Version;
            }
        }

        /// <summary>
        /// Gets a string describing this controller.
        /// </summary>
        public string Description
        {
            get
            {
                return descAttrib.Description;
            }
        }

        /// <summary>
        /// Gets an AuthorAttribute class that contains information about the author.
        /// </summary>
        /// <seealso cref="AuthorAttribute"/>
        public AuthorAttribute AuthorInfo
        {
            get
            {
                return authorAttrib;
            }
        }

        public CapabilitiesAttribute Capabilities
        {
            get
            {
                return capableAttrib;
            }
        }

        /// <summary>
        /// Gets an AcademicInfoAttribute class that contains information about the author's academics.
        /// </summary>
        /// <seealso cref="AcademicInfoAttribute"/>
        public AcademicInfoAttribute AcademicInfo
        {
            get
            {
                return academicAttrib;
            }
        }

        /// <summary>
        /// Gets the file name of the dll that this controller originates from.
        /// </summary>
        public string DLLFileName
        {
            get
            {
                return dllFile;
            }
        }

        /// <summary>
        /// Gets the type class for this controller.
        /// </summary>
        public Type Controller
        {
            get
            {
                return controllerInterface;
            }
        }

        /// <summary>
        /// Loads all of the .dll files located in a given directory and stores information about all
        /// of the controller classes implementing the IBattleshipController interface for future use.
        /// </summary>
        /// <param name="path">The absolute path name to a folder containing DLL files.</param>
        /// <exception cref="DirectoryNotFoundException">The given directory was not found or was a relative path.</exception>
        public static void AddControllerFolder(string path)
        {
            //filePaths should be a list of absolute paths to .dll files
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            var filePaths = new List<string>(Directory.GetFiles(path, "*.dll"));

            foreach (var file in filePaths)
            {
                try
                {
                    var dllInfo = Assembly.LoadFile(file);
                    var types = dllInfo.GetTypes();

                    foreach (Type cont in types)
                    {
                        //Iterating through each class in this assembly.
                        if (cont.IsSubclassOf(typeof(Controller)))
                        {
                            NameAttribute nameAttrib = (NameAttribute)cont.GetCustomAttributes(typeof(NameAttribute), false)[0];
                            VersionAttribute verAttrib = (VersionAttribute)cont.GetCustomAttributes(typeof(VersionAttribute), false)[0];
                            CapabilitiesAttribute capAttrib = (CapabilitiesAttribute)cont.GetCustomAttributes(typeof(CapabilitiesAttribute), false)[0];
                            if (nameAttrib != null && verAttrib != null && capAttrib != null)
                            {
                                //Split the absolute path. We only want the name of the DLL file.
                                string[] pathSplit = file.Split('\\');

                                ControllerInformation info = new ControllerInformation(nameAttrib, verAttrib,
                                    (DescriptionAttribute)cont.GetCustomAttributes(typeof(DescriptionAttribute), false)[0],
                                    (AuthorAttribute)cont.GetCustomAttributes(typeof(AuthorAttribute), false)[0],
                                    (AcademicInfoAttribute)cont.GetCustomAttributes(typeof(AcademicInfoAttribute), false)[0],
                                    capAttrib,
                                    pathSplit[pathSplit.Count() - 1],
                                    cont);
                                loadedInformation.Add(info);
                            }
                            break;
                        }
                    }
                }
                catch
                {
                    //Unable to load a .DLL file; we don't care about this assembly.
                }
            }
        }

        /// <summary>
        /// Clears the currently loaded controllers from the internal list, loads all of the .dll files
        /// located in a given directory, and stores information about all of the controller classes
        /// implementing the IBattleshipController interface for future use.
        /// </summary>
        /// <param name="path">The absolute path name to a folder containing DLL files.</param>
        /// <exception cref="DirectoryNotFoundException">The given directory was not found or was a relative path.</exception>
        public static void LoadControllerFolder(string path)
        {
            loadedInformation = new List<ControllerInformation>();

            AddControllerFolder(path);
        }

        /// <summary>
        /// Generates and returns a string representation for this controller.
        /// </summary>
        public override string ToString()
        {
            return Name + " v(" + Version.ToString() + ")";
        }
    }
}