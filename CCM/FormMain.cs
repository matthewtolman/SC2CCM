﻿using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModManager.StarCraft.Base;
using ModManager.StarCraft.Base.Enums;
using ModManager.StarCraft.Services;

namespace Starcraft_Mod_Manager
{
    public partial class FormMain : Form
    {
        // modLists contains the mods for each campaign (WoL, HotS, LotV, NCO, none)
        //That should be Dictionary<Capaign, List<Mod>> to avoid uninteded duplicates that can slip through testing. Leaving for now. -MajorKaza
        List<Mod>[] modLists = new List<Mod>[5];
        private string sc2BasePath;
        private string importMod = "";

        ZipService zipService;

        public FormMain()
        {
            Log.Information("Started SC2CCM");
            InitializeComponent();
            zipService = new ZipService(logBoxWriteLine); //TODO: Propper logger
        }

        private void SC2MM_Load(object sender, EventArgs e)
        {
            copyUpdater();
            findSC2();
            verifyDirectories();
            zipService.unzipZips(sc2BasePath);
            handleDependencies();
        }
        private void SC2MM_Shown(object sender, EventArgs e)
        {
            populateModLists();
            populateDropdowns();
            setInfoBoxes();
            logBoxWriteLine("Loaded SC2CCM.");
        }

        private void checkForCampaignUpdates()
        {
            // Not gonna do this until it migrates off discord.
        }

        private void copyUpdater()
        {
            if (File.Exists("SC2CCMU.exe"))
            {
                Log.Information("Updated updater found, updating the updater...");
                try
                {
                    File.Delete("SC2CCM Updater.exe");
                    File.Move("SC2CCMU.exe", "SC2CCM Updater.exe");
                } catch (IOException e)
                {
                    Log.Fatal("Failed to delete/move the Updater! Error: {Error}", e.Message);
                    MessageBox.Show("Failed to delete/move the Updater!");
                }
            }
        }

        /* Pulls the StarCraft II directory from whatever program .SC2Save files are opened with (it's often SC2!)
         * If that turns up invalid for some reason or SC2CCM.txt is deleted, it'll ask the user to find it for us.
         * An issue I found that's more common than expected is multiple copies of SC2 being installed.
         * This will only find one of them and it may not be the one that gets launched, leading to "no mods"
         * when mods are selected.
         */
        /// <summary>
        /// Pulls the StarCraft II directory from whatever program .SC2Save files are opened with (it's often SC2!)
        /// </summary>
        public void findSC2()
        {
            string sc2filePath;
            var roamingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var filePath = Path.Combine(roamingDirectory, @"SC2CCM\SC2CCM.txt");
            if (!File.Exists(filePath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                catch (IOException e)
                {
                    Log.Fatal("Unable to create configuration file/folder! Error: {Error}", e.Message);
                    MessageBox.Show("Unable to create configuration file/folder\nTry running this as administrator.", "StarCraft II Custom Campaign Manager");
                    System.Windows.Forms.Application.Exit();
                }
                try
                {
                    // Thanks to xpaperclip for this block.  Assumes directory based on what is pulled up with .SC2Save files.
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Classes\Blizzard.SC2Save\shell\open\command"))
                    {
                        if (key != null)
                        {
                            var value = key.GetValue(null);
                            if (value != null)
                            {
                                var s = value.ToString();
                                s = s.Replace(" \"%1\"", "").Trim('\"');
                                s = Path.GetDirectoryName(s);       // trim off SC2Switcher.exe
                                s = Path.GetDirectoryName(s);       // trim off Support
                                Log.Information("Found StarCraft II exe via regs!");
                                File.WriteAllText(filePath, s + @"\StarCraft II.exe");
                            }
                        }
                    }
                } catch (Exception e)
                {
                    //If we have any issues and need to exit, make the file and force a default path.  We can handle that just ahead.
                    Log.Warning("Unable to detect path with registry! Defaulting to default install location. Error: {Error}", e.Message);
                    File.WriteAllText(filePath, @"C:\Program Files (x86)\StarCraft II\StarCraft II.exe");
                }
            }

            if (!File.Exists(filePath)) //If we have any unknown issues and the file doesn't exist, make it and force a default path.
            {
                Log.Warning("Unable to detect path! Defaulting to default install location");
                File.WriteAllText(filePath, @"C:\Program Files (x86)\StarCraft II\StarCraft II.exe");
            }

            sc2filePath = File.ReadLines(filePath).FirstOrDefault();
            if (!File.Exists(sc2filePath))
            {
                Log.Warning("Could not find path at the config file spot! Prompting user to enter StarCraft II Installation directory");
                MessageBox.Show("It looks like StarCraft II isn't in the default spot!\nPlease use the file browser and select Starcraft II.exe", "StarCraft II Custom Campaign Manager");
                if (findSC2Dialogue.ShowDialog() == DialogResult.OK)
                {
                    //I don't do a check for the filename because I don't know if it appears different in other languages.
                    sc2filePath = findSC2Dialogue.FileName;
                    File.WriteAllText(filePath, sc2filePath);
                } else
                {
                    Log.Fatal("User did not select StarCraft II Installation directory! Exiting application.");
                    MessageBox.Show("No StarCraft II executable was provided. Please make sure StarCraft II is installed and then try again.");
                    Environment.Exit(1);
                }
            }
            sc2BasePath = Path.GetDirectoryName(sc2filePath);
        }

        /* Creates the necessary directories for the CCM to run.
         * Also sets them to not be read only, just in case.
         */
        public void verifyDirectories()
        {
            Log.Verbose("Ensuring we have the correct directories setup with the correct permissions");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign");
            if (!Directory.Exists(sc2BasePath + @"\Maps\CustomCampaigns")) Directory.CreateDirectory(sc2BasePath + @"\Maps\CustomCampaigns");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\swarm")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\swarm");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\swarm\evolution")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\swarm\evolution");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\void")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\void");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\voidprologue")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\voidprologue");
            if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\nova")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\nova");
            if (!Directory.Exists(sc2BasePath + @"\Mods\")) Directory.CreateDirectory(sc2BasePath + @"\Mods\");

            try
            {
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign");
                if (!Directory.Exists(sc2BasePath + @"\Maps\CustomCampaigns")) Directory.CreateDirectory(sc2BasePath + @"\Maps\CustomCampaigns");
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\swarm")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\swarm");
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\swarm\evolution")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\swarm\evolution");
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\void")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\void");
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\voidprologue")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\voidprologue");
                if (!Directory.Exists(sc2BasePath + @"\Maps\Campaign\nova")) Directory.CreateDirectory(sc2BasePath + @"\Maps\Campaign\nova");
                if (!Directory.Exists(sc2BasePath + @"\Mods\")) Directory.CreateDirectory(sc2BasePath + @"\Mods\");

                //I don't think this is necessary, but I'll do it.  Sets all paths to non-read only.
                DirectoryInfo di = new DirectoryInfo(sc2BasePath + @"\Maps\CustomCampaigns");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign\swarm");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign\swarm\evolution");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign\void");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign\voidprologue");
                di.Attributes &= ~FileAttributes.ReadOnly;
                di = new DirectoryInfo(sc2BasePath + @"\Maps\Campaign\nova");
                di.Attributes &= ~FileAttributes.ReadOnly;
            } catch (Exception ex)
            {
                Log.Fatal("Unable to setup StarCraft II directory structure! Error: {Error}", ex.Message);
                MessageBox.Show("We were unable to setup custom campaigns for StarCraft II. Please ensure you have StarCraft II installed and try again.");
                Environment.Exit(1);
            }
        }

        /* Moves all the .SC2Mod files and folders into the Mods folder. 
         * If a .SC2Mod file exists and a .SC2Mod folder is moved in (if the dev updates the method),
         * then an error would occur.  This is handled here.
         */
        public void handleDependencies()
        {
            Log.Verbose("Handling mod dependencies");
            string[] files = Directory.GetFiles(sc2BasePath + @"\Maps\CustomCampaigns\", "*.SC2Mod", SearchOption.AllDirectories);
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                // Folder with same name as file check
                if (Directory.Exists(sc2BasePath + @"\Mods\" + fileName))
                {
                    Directory.Delete(sc2BasePath + @"\Mods\" + fileName, true);
                }
                if (File.Exists(sc2BasePath + @"\Mods\" + fileName))
                {
                    try
                    {
                        File.Delete(sc2BasePath + @"\Mods\" + fileName);
                        File.Move(filePath, sc2BasePath + @"\Mods\" + fileName);
                        logBoxWriteLine("Moved " + fileName + " to Dependencies folder.");
                    } catch (IOException e)
                    {
                        Log.Error("Could not replace file {File}. Error: {Error}", fileName, e);
                        logBoxWriteLine("ERROR: Could not replace " + fileName + " - exit StarCraft II and hit \"Reload\" to fix install properly.");
                    }
                } else
                {
                    try
                    {
                        File.Move(filePath, sc2BasePath + @"\Mods\" + fileName);
                        logBoxWriteLine("Moved " + fileName + " to Dependencies folder.");
                    } catch (IOException e)
                    {
                        Log.Error("Could not move file {File}. Error: {Error}", fileName, e);
                        logBoxWriteLine("ERROR: Could not move " + fileName + " - is it open somewhere?");
                    }
                }
            }

            foreach (string dirPath in Directory.GetDirectories(sc2BasePath + @"\Maps\CustomCampaigns\", "*.SC2Mod", SearchOption.AllDirectories))
            {
                string dirName = Path.GetFileName(dirPath);
                // File with name name as folder check
                if (File.Exists(sc2BasePath + @"\Mods\" + dirName))
                {
                    File.Delete(sc2BasePath + @"\Mods\" + dirName);
                }
                if (Directory.Exists(sc2BasePath + @"\Mods\" + dirName))
                {
                    try
                    {
                        Directory.Delete(sc2BasePath + @"\Mods\" + dirName, true);
                        Directory.Move(dirPath, sc2BasePath + @"\Mods\" + dirName);
                        logBoxWriteLine("Moved " + dirName + " to Dependencies folder.");

                    }
                    catch (IOException e)
                    {
                        Log.Error("Could not replace directory {Directory}. Error: {Error}", dirName, e);
                        logBoxWriteLine("ERROR: Could not replace " + dirName + " - exit StarCraft II and hit \"Reload\" to fix install properly.");
                    }
                } else
                {
                        Directory.Move(dirPath, sc2BasePath + @"\Mods\" + dirName);
                        logBoxWriteLine("Moved " + dirName + " to Dependencies folder.");
                }
            }
        }

        /* Builds the modLists array  based on the folders that exist in the customcampaigns folder. 
         * If a mod does not have a campaign specified in the metadata file, it is assigned to None.
         */
        public void populateModLists()
        {
            //Clean out old mods to start fresh.
            for (int i = 0; i < modLists.Length; i++)
            {
                modLists[i] = new List<Mod>();
            }

            // Search in each directory
            foreach (string dir in Directory.GetDirectories(sc2BasePath + @"\Maps\CustomCampaigns\", "*", SearchOption.TopDirectoryOnly))
            {
                // for a metadata.txt file
                string[] files = Directory.GetFiles(dir, "metadata.txt", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    Log.Error("Failed to load mod, unable to find metadatatxt for {mod}", Path.GetFileName(dir));
                    logBoxWriteLine("FAILED TO LOAD: Unable to find metadata.txt for: " + Path.GetFileName(dir));
                    continue;
                }
                if (files.Length >= 2)
                {
                    Log.Warning("Multiple metadata.txt files found for {mod}! Picking first one we saw", dir);
                    logBoxWriteLine("WARNING: Multiple metadata.txt found for: " + dir);
                }

                Mod currentMod = new Mod();
                foreach (string line in File.ReadLines(files[0]))
                {
                    processLine(line, currentMod);
                }
                currentMod.Path = Path.GetDirectoryName(files[0]);
                modLists[(int)currentMod.Campaign].Add(currentMod);
            }
        }

        /* Adds the mods to the dropdowns menu on the form.
         * Also does a slightly hideous check of comparing the new item to the mod most recently imported.
         * If so, it calls the new mod prompt function.
         */
        public void populateDropdowns()
        {
            wolSelectBox.Items.Clear();
            hotsSelectBox.Items.Clear();
            lotvSelectBox.Items.Clear();
            ncoSelectBox.Items.Clear();

            foreach (Mod mod in modLists[1])
            {
                wolSelectBox.Items.Add(mod.Title);
                if (mod.Path.Contains(@"\" + importMod + @"\") && importMod.Length > 0)
                {
                    promptNewMod(mod.Title, mod.Campaign);
                }
            }
            foreach (Mod mod in modLists[2])
            {
                hotsSelectBox.Items.Add(mod.Title);
                if (mod.Path.Contains(@"\" + importMod + @"\") && importMod.Length > 0)
                {
                    promptNewMod(mod.Title, mod.Campaign);
                }
            }
            foreach (Mod mod in modLists[3])
            {
                lotvSelectBox.Items.Add(mod.Title);
                if (mod.Path.Contains(@"\" + importMod + @"\") && importMod.Length > 0)
                {
                    promptNewMod(mod.Title, mod.Campaign);
                }
            }
            foreach (Mod mod in modLists[4])
            {
                ncoSelectBox.Items.Add(mod.Title);
                if (mod.Path.Contains(@"\" + importMod + @"\") && importMod.Length > 0)
                {
                    promptNewMod(mod.Title, mod.Campaign);
                }
            }
        }

        /* Populates the dropdowns of only a specific campaign.
         * This is called specifically after deleting a campaign.
         */
        public void populateDropdowns(int index)
        {
            if (index == 1)
            {
                wolSelectBox.Items.Clear();
                wolSetButton.Enabled = false;
                wolDeleteButton.Enabled = false;
                foreach (Mod mod in modLists[index])
                {
                    wolSelectBox.Items.Add(mod.Title);
                }
            }

            if (index == 2)
            {
                hotsSelectBox.Items.Clear();
                hotsSetButton.Enabled = false;
                hotsDeleteButton.Enabled = false;
                foreach (Mod mod in modLists[index])
                {
                    hotsSelectBox.Items.Add(mod.Title);
                }
            }

            if (index == 3)
            {
                lotvSelectBox.Items.Clear();
                lotvSetButton.Enabled = false;
                lotvDeleteButton.Enabled = false;
                foreach (Mod mod in modLists[index])
                {
                    lotvSelectBox.Items.Add(mod.Title);
                }
            }

            if (index == 4)
            {
                ncoSelectBox.Items.Clear();
                ncoSetButton.Enabled = false;
                ncoDeleteButton.Enabled = false;
                foreach (Mod mod in modLists[index])
                {
                    ncoSelectBox.Items.Add(mod.Title);
                }
            }
        }

        /* Asks if the user wants to set the single newly imported mod to active.  */
        public void promptNewMod(string modName, Campaign campaign)
        {
            Log.Information("Successfully imported mod {Mod}", modName);
            DialogResult dialogResult = MessageBox.Show("Import of " + modName + " successful, would you like to make it the active campaign?", "StarCraft II Custom Campaign Manager", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                if (campaign == Campaign.WoL)
                {
                    wolSelectBox.SelectedIndex = wolSelectBox.FindStringExact(modName);
                    wolSetButton_Click(null, null);
                }
                else if (campaign == Campaign.HotS)
                {
                    hotsSelectBox.SelectedIndex = hotsSelectBox.FindStringExact(modName);
                    hotsSetButton_Click(null, null);
                }
                else if (campaign == Campaign.LotV)
                {
                    lotvSelectBox.SelectedIndex = lotvSelectBox.FindStringExact(modName);
                    lotvSetButton_Click(null, null);
                }
                else if (campaign == Campaign.NCO)
                {
                    ncoSelectBox.SelectedIndex = ncoSelectBox.FindStringExact(modName);
                    ncoSetButton_Click(null, null);
                }
            }
        }

        /* Sets the display boxes (Title, Author, Version) for the active mod.
         * Sets to a default if no metadata.txt is found. 
         */
        public void setInfoBoxes()
        {
            if (File.Exists(sc2BasePath + @"\Maps\Campaign\metadata.txt"))
            {
                Mod activeMod = new Mod();
                foreach (string line in File.ReadLines(sc2BasePath + @"\Maps\Campaign\metadata.txt"))
                {
                    processLine(line, activeMod);
                }
                wolTitleBox.Text = activeMod.Title;
                wolAuthorBox.Text = activeMod.Author;
                wolVersionBox.Text = activeMod.Version;
            }
            else
            {
                wolTitleBox.Text = "Default Campaign";
                wolAuthorBox.Text = "Blizzard";
                wolVersionBox.Text = "N/A";
            }

            if (File.Exists(sc2BasePath + @"\Maps\Campaign\swarm\metadata.txt"))
            {
                Mod activeMod = new Mod();
                foreach (string line in File.ReadLines(sc2BasePath + @"\Maps\Campaign\swarm\metadata.txt"))
                {
                    processLine(line, activeMod);
                }
                hotsTitleBox.Text = activeMod.Title;
                hotsAuthorBox.Text = activeMod.Author;
                hotsVersionBox.Text = activeMod.Version;
            }
            else
            {
                hotsTitleBox.Text = "Default Campaign";
                hotsAuthorBox.Text = "Blizzard";
                hotsVersionBox.Text = "N/A";
            }

            if (File.Exists(sc2BasePath + @"\Maps\Campaign\void\metadata.txt"))
            {
                Mod activeMod = new Mod();
                foreach (string line in File.ReadLines(sc2BasePath + @"\Maps\Campaign\void\metadata.txt"))
                {
                    processLine(line, activeMod);
                }
                lotvTitleBox.Text = activeMod.Title;
                lotvAuthorBox.Text = activeMod.Author;
                lotvVersionBox.Text = activeMod.Version;
            }
            else
            {
                lotvTitleBox.Text = "Default Campaign";
                lotvAuthorBox.Text = "Blizzard";
                lotvVersionBox.Text = "N/A";
            }

            if (File.Exists(sc2BasePath + @"\Maps\Campaign\nova\metadata.txt"))
            {
                Mod activeMod = new Mod();
                foreach (string line in File.ReadLines(sc2BasePath + @"\Maps\Campaign\nova\metadata.txt"))
                {
                    processLine(line, activeMod);
                }
                ncoTitleBox.Text = activeMod.Title;
                ncoAuthorBox.Text = activeMod.Author;
                ncoVersionBox.Text = activeMod.Version;
            }
            else
            {
                ncoTitleBox.Text = "Default Campaign";
                ncoAuthorBox.Text = "Blizzard";
                ncoVersionBox.Text = "N/A";
            }
        }

        /* Handles a line pulled from metadata.txt and stores it in the mod object. */
        private void processLine(string line, Mod mod)
        {
            String[] lineParts = line.Split(new[] { '=' }, 2);
            if (lineParts[0].ToLower() == "title") mod.Title = lineParts[1];
            if (lineParts[0].ToLower() == "desc") mod.Description = lineParts[1];
            if (lineParts[0].ToLower() == "campaign") mod.SetCampaign(lineParts[1]);
            if (lineParts[0].ToLower() == "version") mod.Version = lineParts[1];
            if (lineParts[0].ToLower() == "author") mod.Author = lineParts[1];
            //if (lineParts[0].ToLower() == "lotvprologue") mod.SetPrologue(lineParts[1]);
        }

        /* Recursively copies all files and folders of a source folder to a target folder.
         */
        private static void copyFilesAndFolders(string sourcePath, string targetPath)
        {
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
                Log.Verbose("Created directory at {Dir}", dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }

        /* Clears a directively recursively. */
        public bool clearDir(string dir)
        {
            bool retVal = true;
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException e)
                    {
                        Log.Error("Unable to delete campaign file. Error: {Error}", e);
                        logBoxWriteLine("ERROR: Could not delete campaign file " + Path.GetFileNameWithoutExtension(file) + " - please exit the campaign and try again.");
                        retVal = false;
                    }
                }
            } catch (IOException e)
            {
                Log.Error("Did not find directory {Dir} to clear. Error: {Error}", dir, e);
                logBoxWriteLine("ERROR: Did not find directory " + dir + " to clear.");
            }
            //TODO: Fix the evo missions
            foreach (string subdir in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!(subdir.Contains(@"Campaign\swarm") || subdir.Contains(@"Campaign\void") || subdir.Contains(@"Campaign\nova")))
                {
                    Directory.Delete(subdir, true);
                }
            }
            return retVal;
        }

        private void wolSelectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            wolSetButton.Enabled = true;
            wolDeleteButton.Enabled = true;
        }

        private void hotsSelectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            hotsSetButton.Enabled = true;
            hotsDeleteButton.Enabled = true;
        }

        private void lotvSelectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            lotvSetButton.Enabled = true;
            lotvDeleteButton.Enabled = true;
        }

        private void ncoSelectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ncoSetButton.Enabled = true;
            ncoDeleteButton.Enabled = true;
        }

        private void logBoxWriteLine(string message)
        {
            logBox.Text += message;
            logBox.SelectionStart = logBox.Text.Length;
            logBox.ScrollToCaret();
            logBox.Text += "\n";
            Log.Debug("User-facing message: {Message}", message);
            //logBox.Items.Add(message);
            //int nItems = (int)(logBox.Height / logBox.ItemHeight);
            //logBox.TopIndex = logBox.Items.Count - nItems;
        }

        private void displayBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
        }

        /* 
         * I've opted to keep all of these as separate functions instead of passing them into a larger "handleSetButton" function.
         * Because the directory structure is a little wonky, I'd probably have to pass in the directory and some string helpers as well.
         * To avoid this, I'd probably have to make a whole set of extra helper functions and change some of my setup, which I don't have time to do.
         * It is probably easier to maintain as one larger function, but I'm not at the point where I can really do that.
         */
        private void wolSetButton_Click(object sender, EventArgs e)
        {
            if (wolSelectBox.SelectedIndex < 0) return;
            Mod selectedMod = modLists[1][wolSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            if (clearDir(sc2BasePath + @"\Maps\Campaign"))
            {
                copyFilesAndFolders(modPath, sc2BasePath + @"\Maps\Campaign");
                setInfoBoxes();
                Log.Information("Wings of Liberty campaign changed to {Title}", selectedMod.Title);
                logBoxWriteLine("Set Wings Campaign to " + selectedMod.Title + "!");
                hideWarningImg(wolWarningImg);
            } else
            {
                Log.Error("Failed to change Wings of Liberty campaign to {Title}", selectedMod.Title);
                logBoxWriteLine("ERROR: Could not set Wings campaign - SC2 files in use.");
                showWarningImg(wolWarningImg);
            }
        }

        private void wolDeleteButton_Click(object sender, EventArgs e)
        {
            Mod selectedMod = modLists[1][wolSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete " + selectedMod.Title + "?", "StarCraft II Custom Campaign Manager", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                while (!Path.GetDirectoryName(modPath).EndsWith("CustomCampaigns"))
                {
                    modPath = Path.GetDirectoryName(modPath);
                }
                if (clearDir(modPath))
                {
                    Directory.Delete(modPath);
                    populateModLists();
                    populateDropdowns((int)Campaign.WoL);
                    setInfoBoxes();
                    Log.Information("Deleted WoL mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("Deleted " + selectedMod.Title + " from local storage.");
                }
                else
                {
                    Log.Error("Failed to delete WoL mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("ERROR: Could not delete " + selectedMod.Title + " - a file may be open somewhere.");
                }
            }
        }

        private void wolRestoreButton_Click(object sender, EventArgs e)
        {
            logBoxWriteLine("Resetting Wings Campaign to default.");
            if (clearDir(sc2BasePath + @"\Maps\Campaign"))
            {
                Log.Information("Reset Wings of Liberty campaign.");
                logBoxWriteLine("Clear successful!");
                setInfoBoxes();
                hideWarningImg(wolWarningImg);
            } else
            {
                Log.Error("Failed to reset Wings of Liberty campaign.");
                logBoxWriteLine("ERROR: Could not set Wings campaign - SC2 files in use.");
                showWarningImg(wolWarningImg);
            }
        }


        private void hotsSetButton_Click(object sender, EventArgs e)
        {
            if (hotsSelectBox.SelectedIndex < 0) return;
            Mod selectedMod = modLists[2][hotsSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            if (clearDir(sc2BasePath + @"\Maps\Campaign\swarm"))
            {
                copyFilesAndFolders(modPath, sc2BasePath + @"\Maps\Campaign\swarm");
                setInfoBoxes();
                Log.Information("Heart of the Swarm campaign changed to {Title}", selectedMod.Title);
                logBoxWriteLine("Set Swarm Campaign to " + selectedMod.Title + "!");
                hideWarningImg(hotsWarningImg);
            }
            else
            {
                Log.Error("Failed to change Heart of the Swarm campaign to {Title}", selectedMod.Title);
                logBoxWriteLine("ERROR: Could not set Swarm campaign - SC2 files in use.");
                showWarningImg(hotsWarningImg);
            }
        }

        private void hotsDeleteButton_Click(object sender, EventArgs e)
        {
            Mod selectedMod = modLists[2][hotsSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete " + selectedMod.Title + "?", "StarCraft II Custom Campaign Manager", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                while (!Path.GetDirectoryName(modPath).EndsWith("CustomCampaigns"))
                {
                    modPath = Path.GetDirectoryName(modPath);
                }
                if (clearDir(modPath))
                {
                    Directory.Delete(modPath);
                    populateModLists();
                    populateDropdowns((int)Campaign.HotS);
                    setInfoBoxes();
                    Log.Information("Deleted HotS mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("Deleted " + selectedMod.Title + " from local storage.");
                }
                else
                {
                    Log.Error("Failed to delete HotS mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("ERROR: Could not delete " + selectedMod.Title + " - a file may be open somewhere.");
                }
            }
        }

        private void hotsRestoreButton_Click(object sender, EventArgs e)
        {
            logBoxWriteLine("Resetting Swarm Campaign to default.");
            if (clearDir(sc2BasePath + @"\Maps\Campaign\swarm") && clearDir(sc2BasePath + @"\Maps\Campaign\swarm\evolution"))
            {
                Log.Information("Reset Heart of the Swarm campaign.");
                logBoxWriteLine("Clear complete!");
                setInfoBoxes();
                hideWarningImg(hotsWarningImg);
            }
            else
            {
                Log.Error("Failed to reset Heart of the Swarm campaign.");
                logBoxWriteLine("ERROR: Could not set Swarm campaign - SC2 files in use.");
                showWarningImg(hotsWarningImg);
            }
        }

        private void lotvSetButton_Click(object sender, EventArgs e)
        {
            if (lotvSelectBox.SelectedIndex < 0) return;
            Mod selectedMod = modLists[3][lotvSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            if (clearDir(sc2BasePath + @"\Maps\Campaign\void"))
            {
                copyFilesAndFolders(modPath, sc2BasePath + @"\Maps\Campaign\void");
                setInfoBoxes();
                Log.Information("Legacy of the Void campaign changed to {Title}", selectedMod.Title);
                logBoxWriteLine("Set Void Campaign to " + selectedMod.Title + "!");
                hideWarningImg(lotvWarningImg);
            }
            else
            {
                Log.Error("Failed to change Legacy of the Void campaign to {Title}", selectedMod.Title);
                logBoxWriteLine("ERROR: Could not set Void campaign - SC2 files in use.");
                showWarningImg(lotvWarningImg);
            }
        }

        private void lotvDeleteButton_Click(object sender, EventArgs e)
        {
            Mod selectedMod = modLists[3][lotvSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete " + selectedMod.Title + "?", "StarCraft II Custom Campaign Manager", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                while (!Path.GetDirectoryName(modPath).EndsWith("CustomCampaigns"))
                {
                    modPath = Path.GetDirectoryName(modPath);
                }
                if (clearDir(modPath))
                {
                    Directory.Delete(modPath);
                    populateModLists();
                    populateDropdowns((int)Campaign.LotV);
                    setInfoBoxes();
                    Log.Information("Deleted LotV mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("Deleted " + selectedMod.Title + " from local storage.");
                }
                else
                {
                    Log.Error("Failed to delete LotV mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("ERROR: Could not delete " + selectedMod.Title + " - a file may be open somewhere.");
                }
            }
        }

        private void lotvRestoreButton_Click(object sender, EventArgs e)
        {
            logBoxWriteLine("Resetting Void Campaign to default.");
            if (clearDir(sc2BasePath + @"\Maps\Campaign\void"))
            {
                Log.Information("Reset Legacy of the Void campaign.");
                logBoxWriteLine("Clear complete!");
                setInfoBoxes();
                hideWarningImg(lotvWarningImg);
            }
            else
            {
                Log.Error("Failed to reset Legacy of the Void campaign.");
                logBoxWriteLine("ERROR: Could not set Void campaign - SC2 files in use.");
                showWarningImg(lotvWarningImg);
            }
        }

        private void ncoSetButton_Click(object sender, EventArgs e)
        {
            if (ncoSelectBox.SelectedIndex < 0) return;
            Mod selectedMod = modLists[4][ncoSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            if (clearDir(sc2BasePath + @"\Maps\Campaign\nova"))
            {
                copyFilesAndFolders(modPath, sc2BasePath + @"\Maps\Campaign\nova");
                setInfoBoxes();
                Log.Information("Nova Covert Ops campaign changed to {Title}", selectedMod.Title);
                logBoxWriteLine("Set Nova Campaign to " + selectedMod.Title + "!");
                hideWarningImg(ncoWarningImg);
            }
            else
            {
                Log.Error("Failed to change Nova Covert Ops campaign to {Title}", selectedMod.Title);
                logBoxWriteLine("ERROR: Could not set Nova campaign - SC2 files in use.");
                showWarningImg(ncoWarningImg);
            }
        }

        private void ncoDeleteButton_Click(object sender, EventArgs e)
        {
            Mod selectedMod = modLists[4][ncoSelectBox.SelectedIndex];
            string modPath = selectedMod.Path;
            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete " + selectedMod.Title + "?", "StarCraft II Custom Campaign Manager", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                while (!Path.GetDirectoryName(modPath).EndsWith("CustomCampaigns"))
                {
                    modPath = Path.GetDirectoryName(modPath);
                }
                if (clearDir(modPath))
                {
                    Directory.Delete(modPath);
                    populateModLists();
                    populateDropdowns((int)Campaign.NCO);
                    setInfoBoxes();
                    Log.Information("Deleted NCO mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("Deleted " + selectedMod.Title + " from local storage.");
                }
                else
                {
                    Log.Error("Failed to delete NCO mod {Title} from local storage", selectedMod.Title);
                    logBoxWriteLine("ERROR: Could not delete " + selectedMod.Title + " - a file may be open somewhere.");
                }
            }
        }

        private void ncoRestoreButton_Click(object sender, EventArgs e)
        {
            logBoxWriteLine("Resetting Nova Campaign to default.");
            if (clearDir(sc2BasePath + @"\Maps\Campaign\nova"))
            {
                Log.Information("Reset Legacy of the Void campaign.");
                logBoxWriteLine("Clear complete!");
                setInfoBoxes();
                hideWarningImg(ncoWarningImg);
            }
            else
            {
                Log.Error("Failed to reset Legacy of the Void campaign.");
                logBoxWriteLine("ERROR: Could not set Nova campaign - SC2 files in use.");
                showWarningImg(ncoWarningImg);
            }
        }

        private void infoButton_Click(object sender, EventArgs e)
        {
            Log.Debug("Clicked info button");
            MessageBox.Show("To Install a Custom Campaign:\n" +
                "1. Drag and drop the mod .zip file onto the Custom Campaign Manager\n" +
                "2. Select the custom campaign from the dropdown list and hit \"Set to Active Campaign\"", "Starcraft II Custom Campaign Manager");
        }
        private void importButton_Click(object sender, EventArgs e)
        {
            Log.Debug("Clicked import button");
            string[] targetFilePaths;
            selectFolderDialogue.Filter = "zip archives (*.zip)|*.zip";
            if (selectFolderDialogue.ShowDialog() == DialogResult.OK)
            {
                targetFilePaths = selectFolderDialogue.FileNames.ToArray<String>();
                importFiles(targetFilePaths);
            }
        }

        private void installButton_Click(object sender, EventArgs e)
        {
            Log.Debug("Clicked install button");
            SC2MM_Load(null, null);
            SC2MM_Shown(null, null);
        }

        private void SC2CCM_DragDrop(object sender, DragEventArgs e)
        {
            Log.Debug("Files dragged and dropped");
            string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            importFiles(filePaths);
        }

        /* Copies all files from the specified folder into idfk lets do this tomorrow
         * TODO 
         */
        private void importFiles(string[] filePaths)
        {
            int importCount = 0;

            foreach (String filePath in filePaths)
            {
                if (filePath.ToLower().EndsWith(".zip"))
                {
                    importCount++;
                    // This is NOT a good solution, but it should work.
                    if (importCount >= 2)
                    {
                        importMod = "";
                    }
                    else
                    {
                        importMod = Path.GetFileNameWithoutExtension(filePath);
                    }
                    File.Copy(filePath, sc2BasePath + @"\Maps\CustomCampaigns\" + Path.GetFileName(filePath), true);
                }
                else
                {
                    if (Path.GetExtension(filePath).Length == 0)
                    {
                        importCount++;
                        // This is NOT a good solution, but it should work.
                        if (importCount >= 2)
                        {
                            importMod = "";
                        }
                        else
                        {
                            importMod = Path.GetFileNameWithoutExtension(filePath);
                        }
                        string targetDir = sc2BasePath + @"\Maps\CustomCampaigns\" + Path.GetFileName(filePath);
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        copyFilesAndFolders(filePath, targetDir);
                        Directory.Delete(filePath, true);
                    }
                }
            }
            SC2MM_Load(null, null);
            SC2MM_Shown(null, null);
        }

        private void SC2CCM_DragEnter(object sender, DragEventArgs e)
        {
            string filename;
            bool valid;
            valid = GetFilename(out filename, e);

            if (valid)
            {
                e.Effect = DragDropEffects.Move;
            } else
            {
                logBoxWriteLine("Debug: Drag Enter with fn " + filename + " invalid.");
                e.Effect = DragDropEffects.None;
            }
            
        }

        private void SC2CCM_DragLeave(object sender, EventArgs e)
        {
            //e.Effect = DragDropEffects.Move;
        }

        private void SC2CCM_DragOver(object sender, DragEventArgs e)
        {
            string filename;
            bool valid;
            valid = GetFilename(out filename, e);

            if (valid)
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        protected bool GetFilename(out string filename, DragEventArgs e)
        {
            bool ret = false;
            filename = String.Empty;

            if ((e.AllowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
            {
                Array data = ((IDataObject)e.Data).GetData("FileName") as Array;
                if (data != null)
                {
                    if ((data.Length == 1) && (data.GetValue(0) is String))
                    {
                        filename = ((string[])data)[0];
                        string ext = Path.GetExtension(filename).ToLower();
                        Console.WriteLine("Ext: " + ext);
                        if (ext == ".zip" || ext == "")
                        {
                            ret = true;
                        }
                    }
                }
            }
            return ret;
        }

        private void handleWarningHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip((PictureBox)sender, "Warning: Active campaign may have missing or inconsistent files.\nThis usually due to changing campaigns while in a mission or menu.\nPlease exit to the menu in SC2 and set an active mod to clear this.");
        }

        private void showWarningImg(PictureBox warningImg)
        {
            warningImg.Visible = true;
        }

        private void hideWarningImg(PictureBox warningImg)
        {
            warningImg.Visible = false;
        }

        private void logBox_KeyDown(object sender, KeyEventArgs e)
        {
            //e.SuppressKeyPress = true;
        }
    }
}
