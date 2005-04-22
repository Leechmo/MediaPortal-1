using System;
using System.Collections;
using System.IO;
using MediaPortal.Dialogs;
using MediaPortal.GUI.Library;
using MediaPortal.Util;
using Programs.Utils;
using SQLite.NET;

namespace ProgramsDatabase
{
	/// <summary>
	/// Summary description for appItemDirCache.
	/// </summary>
	public class appItemDirCache: AppItem
	{

		GUIDialogProgress pDlgProgress = null;
		
		public appItemDirCache(SQLiteClient paramDB): base(paramDB)
		{
		}

		private void ShowProgressDialog()
		{
			pDlgProgress = (GUIDialogProgress)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_PROGRESS);
			pDlgProgress.SetHeading("scanning directory");
			pDlgProgress.SetLine(0, "scanning directory");		//todo: localize! 13004...
			pDlgProgress.SetLine(1, "");
			pDlgProgress.SetLine(2, "");
			pDlgProgress.StartModal(GetID);
			pDlgProgress.Progress();
		}

		private void CloseProgressDialog()
		{
			pDlgProgress.Close();
		}

		private string GetThumbsFile(GUIListItem guiFile, string fileTitle)
		{
			string strFolderThumb = "";
			if (ImageDirs.Length > 0)
			{

				string strMainImgDir = ImageDirs[0];

				string strDir = strMainImgDir + "\\";
				string strFilenameNoExt = strMainImgDir + "\\" + guiFile.Label;
				strFilenameNoExt = Path.ChangeExtension(strFilenameNoExt, null);
				strFilenameNoExt = Path.GetFileNameWithoutExtension(strFilenameNoExt);

				string[] strExactMatchesJPG = Directory.GetFiles(strDir, strFilenameNoExt+"*.jpg");
				string[] strExactMatchesGIF = Directory.GetFiles(strDir, strFilenameNoExt+"*.gif");
				string[] strExactMatchesPNG = Directory.GetFiles(strDir, strFilenameNoExt+"*.png");
				if (strExactMatchesJPG.Length > 0)
				{
					strFolderThumb = strExactMatchesJPG[0];
				}
				else if (strExactMatchesGIF.Length > 0)
				{
					strFolderThumb = strExactMatchesGIF[0];
				}
				else if (strExactMatchesPNG.Length > 0)
				{
					strFolderThumb = strExactMatchesPNG[0];
				}
				else 
				{
					// no exact match found! Redo with near matches!
					string[] strNearMatchesJPG = Directory.GetFiles(strDir, fileTitle+"*.jpg");
					string[] strNearMatchesGIF = Directory.GetFiles(strDir, fileTitle+"*.gif");
					string[] strNearMatchesPNG = Directory.GetFiles(strDir, fileTitle+"*.png");
					if (strNearMatchesJPG.Length > 0)
					{
						strFolderThumb = strNearMatchesJPG[0];
					}
					else if (strNearMatchesGIF.Length > 0)
					{
						strFolderThumb = strNearMatchesGIF[0];
					}
					else if (strNearMatchesPNG.Length > 0)
					{
						strFolderThumb = strNearMatchesPNG[0];
					}
				}
				
			}
			return strFolderThumb;
		}

		

		private void ImportFileItem(GUIListItem guiFile)
		{
			FileItem curFile = new FileItem(m_db);
			curFile.FileID = -1; // to force an INSERT statement when writing the item
			curFile.AppID = this.AppID;
			curFile.Title = guiFile.Label;
			curFile.Title = curFile.TitleNormalized;
			curFile.Filename = guiFile.Path;
			if (this.UseQuotes)
			{
				curFile.Filename = "\"" + curFile.Filename + "\"";
			}
			curFile.Filepath = Path.GetDirectoryName(guiFile.Path);
			curFile.Imagefile = GetThumbsFile(guiFile, curFile.TitleNormalized);
			// not imported properties => set default values
			curFile.ManualFilename = "";
			curFile.LastTimeLaunched = DateTime.MinValue;
			curFile.LaunchCount = 0;
			curFile.Write();
		}

		private void WriteFolderItem(string directoryPath)
		{
			FileItem curFile = new FileItem(m_db);
			curFile.FileID = -1;
			curFile.AppID = this.AppID;
			curFile.Filename = directoryPath;
			curFile.Title = Path.GetFileNameWithoutExtension(directoryPath);
			curFile.Filepath = Path.GetDirectoryName(directoryPath);
			curFile.IsFolder = true;
			curFile.ManualFilename = "";
			curFile.LastTimeLaunched = DateTime.MinValue;
			curFile.LaunchCount = 0;
			curFile.Write();
		}





		private void UpdateProgressDialog(GUIListItem guiFile, bool bGUIMode)
		{
			if (bGUIMode)
			{
				pDlgProgress.SetLine(2, String.Format("{0} {1}", GUILocalizeStrings.Get(13005), guiFile.Label)); // "last imported file {0}"
				pDlgProgress.Progress();
			}
			SendRefreshInfo(String.Format("{0} {1}", GUILocalizeStrings.Get(13005), guiFile.Label));
		}

		private void ImportDirectory(string curPath, bool bGUIMode)
		{
			VirtualDirectory  m_directory = new VirtualDirectory();
			ArrayList mExtensions = new ArrayList( this.ValidExtensions.Split( ',' ) );
			m_directory.SetExtensions(mExtensions);

			// read files
			ArrayList arrFiles = m_directory.GetDirectory( curPath );
			foreach (GUIListItem file in arrFiles)
			{
				if (!file.IsFolder)
				{
					ImportFileItem(file);
					UpdateProgressDialog(file, bGUIMode);
				}
			}

			//read subdirectories
			try
			{
				string[] directories = Directory.GetDirectories(curPath);
				foreach(string directory in directories)
				{
					WriteFolderItem(directory);
					// recursively call importer for every subdirectory
					ImportDirectory(directory, bGUIMode);
				}
			}
			catch
			{
				// Ignore
			}

		}

		private void DoDirCacheImport(bool bGUIMode)
		{
			if (m_db==null) return;
			if (this.AppID < 0) return;
			if (this.SourceType != myProgSourceType.DIRCACHE) return;
			if (bGUIMode)
			{
				ShowProgressDialog();
			}
			try
			{
				ValidExtensions = ValidExtensions.Replace(" ", "");
				ImportDirectory(this.FileDirectory, bGUIMode);
			}
			finally
			{
				if (bGUIMode)
				{
					CloseProgressDialog();
				}
			}

		}


		override public void LoadFiles()
		{
			// load Files and fill Files-arraylist here!
			if (mFiles == null) 
			{
				mFiles = new Filelist(m_db);}
			else 
			{ 
				mFiles.Clear();
			}
			mFiles.Load(AppID, FileDirectory);
			bFilesLoaded = true;
		}



		override public string CurrentFilePath()
		{
			if (Files.Filepath != "")
			{
				return Files.Filepath;
			}
			else 
			{
				return base.CurrentFilePath();
			}
		}


		override public string DefaultFilepath()
		{
			return this.FileDirectory; 
		}

		override public bool RefreshButtonVisible()
		{
			return true;
		}


		override public bool FileBrowseAllowed()
		{
			return true;  
		}

		override public bool ProfileLoadingAllowed()
		{
			return true;
		}

		override public void Refresh(bool bGUIMode)
		{
			base.Refresh(bGUIMode);
			DeleteFiles();
			DoDirCacheImport(bGUIMode);
			FixFileLinks();
			LoadFiles();
		}
    }

}
