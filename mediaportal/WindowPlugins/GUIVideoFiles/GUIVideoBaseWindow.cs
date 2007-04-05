#region Copyright (C) 2005-2007 Team MediaPortal

/* 
 *	Copyright (C) 2005-2007 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */

#endregion

using System;
using System.Collections;
using System.Net;
using System.Globalization;
using MediaPortal.GUI.Library;
using MediaPortal.Util;
using MediaPortal.Player;
using MediaPortal.Playlists;
using MediaPortal.Video.Database;
using MediaPortal.Dialogs;
using MediaPortal.GUI.View;
using MediaPortal.Configuration;
using MediaPortal.Services;

namespace MediaPortal.GUI.Video
{
  /// <summary>
  /// Summary description for GUIVideoBaseWindow.
  /// </summary>
  public abstract class GUIVideoBaseWindow : GUIWindow
  {

    public enum View
    {
      List = 0,
      Icons = 1,
      LargeIcons = 2,
      FilmStrip = 3
    }

    protected View currentView = View.List;
    protected View currentViewRoot = View.List;
    protected VideoSort.SortMethod currentSortMethod = VideoSort.SortMethod.Name;
    protected VideoSort.SortMethod currentSortMethodRoot = VideoSort.SortMethod.Name;
    protected bool m_bSortAscending;
    protected bool m_bSortAscendingRoot;
    protected VideoViewHandler handler;
    protected string _playListPath = String.Empty;
    protected string _currentFolder = String.Empty;
    protected string _lastFolder = String.Empty;


    [SkinControlAttribute(50)]   protected GUIFacadeControl facadeView = null;
    [SkinControlAttribute(2)]    protected GUIButtonControl btnViewAs = null;
    [SkinControlAttribute(3)]    protected GUISortButtonControl btnSortBy = null;
    [SkinControlAttribute(5)]    protected GUIButtonControl btnViews = null;
    [SkinControlAttribute(6)]    protected GUIButtonControl btnPlayDVD = null;
    [SkinControlAttribute(8)]    protected GUIButtonControl btnTrailers = null;
    [SkinControlAttribute(9)]    protected GUIButtonControl btnPlaylistFolder = null;

    protected PlayListPlayer playlistPlayer;

    public GUIVideoBaseWindow()
    {
      handler = new VideoViewHandler();
      playlistPlayer = PlayListPlayer.SingletonPlayer;
    }

    protected virtual bool AllowView(View view)
    {
      return true;
    }
    protected virtual bool AllowSortMethod(VideoSort.SortMethod method)
    {
      return true;
    }
    protected virtual View CurrentView
    {
      get { return currentView; }
      set { currentView = value; }
    }

    protected virtual VideoSort.SortMethod CurrentSortMethod
    {
      get { return currentSortMethod; }
      set { currentSortMethod = value; }
    }
    protected virtual bool CurrentSortAsc
    {
      get { return m_bSortAscending; }
      set { m_bSortAscending = value; }
    }

    protected virtual string SerializeName
    {
      get
      {
        return String.Empty;
      }
    }
    #region Serialisation
    protected virtual void LoadSettings()
    {
      using (MediaPortal.Profile.Settings xmlreader = new MediaPortal.Profile.Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml")))
      {
        currentView = (View)xmlreader.GetValueAsInt(SerializeName, "view", (int)View.List);
        currentViewRoot = (View)xmlreader.GetValueAsInt(SerializeName, "viewroot", (int)View.List);

        currentSortMethod = (VideoSort.SortMethod)xmlreader.GetValueAsInt(SerializeName, "sortmethod", (int)VideoSort.SortMethod.Name);
        currentSortMethodRoot = (VideoSort.SortMethod)xmlreader.GetValueAsInt(SerializeName, "sortmethodroot", (int)VideoSort.SortMethod.Name);
        m_bSortAscending = xmlreader.GetValueAsBool(SerializeName, "sortasc", true);
        m_bSortAscendingRoot = xmlreader.GetValueAsBool(SerializeName, "sortascroot", true);

        _playListPath = xmlreader.GetValueAsString("movies", "playlists", String.Empty);
        _playListPath = MediaPortal.Util.Utils.RemoveTrailingSlash(_playListPath);

      }

      SwitchView();
    }

    protected virtual void SaveSettings()
    {
      using (MediaPortal.Profile.Settings xmlwriter = new MediaPortal.Profile.Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml")))
      {
        xmlwriter.SetValue(SerializeName, "view", (int)currentView);
        xmlwriter.SetValue(SerializeName, "viewroot", (int)currentViewRoot);
        xmlwriter.SetValue(SerializeName, "sortmethod", (int)currentSortMethod);
        xmlwriter.SetValue(SerializeName, "sortmethodroot", (int)currentSortMethodRoot);
        xmlwriter.SetValueAsBool(SerializeName, "sortasc", m_bSortAscending);
        xmlwriter.SetValueAsBool(SerializeName, "sortascroot", m_bSortAscendingRoot);
      }
    }
    #endregion

    protected bool ViewByIcon
    {
      get
      {
        if (CurrentView != View.List)
          return true;
        return false;
      }
    }

    protected bool ViewByLargeIcon
    {
      get
      {
        if (CurrentView == View.LargeIcons)
          return true;
        return false;
      }
    }
    public override void OnAction(Action action)
    {
      if (action.wID == Action.ActionType.ACTION_SHOW_PLAYLIST)
      {
        GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_VIDEO_PLAYLIST);
        return;
      }
      base.OnAction(action);
    }

    protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
    {
      if (control == btnViewAs)
      {
        bool shouldContinue = false;
        do
        {
          shouldContinue = false;
          switch (CurrentView)
          {
            case View.List:
              CurrentView = View.Icons;
              if (!AllowView(CurrentView) || facadeView.ThumbnailView == null)
                shouldContinue = true;
              else
                facadeView.View = GUIFacadeControl.ViewMode.SmallIcons;
              break;
            case View.Icons:
              CurrentView = View.LargeIcons;
              if (!AllowView(CurrentView) || facadeView.ThumbnailView == null)
                shouldContinue = true;
              else
                facadeView.View = GUIFacadeControl.ViewMode.LargeIcons;
              break;
            case View.LargeIcons:
              CurrentView = View.FilmStrip;
              if (!AllowView(CurrentView) || facadeView.FilmstripView == null)
                shouldContinue = true;
              else
                facadeView.View = GUIFacadeControl.ViewMode.Filmstrip;
              break;
            case View.FilmStrip:
              CurrentView = View.List;
              if (!AllowView(CurrentView) || facadeView.ListView == null)
                shouldContinue = true;
              else
                if (GUIWindowManager.ActiveWindow == (int)GUIWindow.Window.WINDOW_VIDEO_PLAYLIST)
                  facadeView.View = GUIFacadeControl.ViewMode.Playlist;
                else
                  facadeView.View = GUIFacadeControl.ViewMode.List;
              break;
          }
        } while (shouldContinue);
        SelectCurrentItem();
        GUIControl.FocusControl(GetID, controlId);
        return;
      }//if (control == btnViewAs)

      if (control == btnSortBy)
      {
        OnShowSortOptions();
      }//if (control==btnSortBy)


      if (control == btnViews)
      {
        OnShowViews();
      }


      if (control == btnPlayDVD)
      {
        ISelectDVDHandler selectDVDHandler;
        if (GlobalServiceProvider.IsRegistered<ISelectDVDHandler>())
        {
          selectDVDHandler = GlobalServiceProvider.Get<ISelectDVDHandler>();
        }
        else
        {
          selectDVDHandler = new SelectDVDHandler();
          GlobalServiceProvider.Add<ISelectDVDHandler>(selectDVDHandler);
        }
        string dvdToPlay = selectDVDHandler.ShowSelectDVDDialog(GetID);
        if (dvdToPlay != null)
        {
          selectDVDHandler.OnPlayDVD(dvdToPlay, GetID);
        }
        return;
      }

      if (control == facadeView)
      {
        GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_ITEM_SELECTED, GetID, 0, controlId, 0, 0, null);
        OnMessage(msg);
        int iItem = (int)msg.Param1;
        if (actionType == Action.ActionType.ACTION_SHOW_INFO)
        {
          OnInfo(iItem);
          facadeView.RefreshCoverArt();
        }
        if (actionType == Action.ActionType.ACTION_SELECT_ITEM)
        {
          OnClick(iItem);
        }
        if (actionType == Action.ActionType.ACTION_QUEUE_ITEM)
        {
          OnQueueItem(iItem);
        }
      }

      if (control == btnPlaylistFolder)
      {
        if (_currentFolder != _playListPath)
        {
          _lastFolder = _currentFolder;
          _currentFolder = _playListPath;
        }
        else
        {
          _currentFolder = _lastFolder;
        }
        LoadDirectory(_currentFolder);
        return;
      }
    }
    
    protected void SelectCurrentItem()
    {
      int iItem = facadeView.SelectedListItemIndex;
      if (iItem > -1)
      {
        GUIControl.SelectItemControl(GetID, facadeView.GetID, iItem);
      }
      UpdateButtonStates();
    }

    protected virtual void UpdateButtonStates()
    {
      GUIPropertyManager.SetProperty("#view", handler.LocalizedCurrentView);
      if (GetID == (int)GUIWindow.Window.WINDOW_VIDEO_TITLE)
      {
        GUIPropertyManager.SetProperty("#currentmodule", String.Format("{0}/{1}", GUILocalizeStrings.Get(100006), handler.LocalizedCurrentView));
      }
      else
      {
        GUIPropertyManager.SetProperty("#currentmodule", GUILocalizeStrings.Get(100000 + GetID));
      }

      GUIControl.HideControl(GetID, facadeView.GetID);

      int iControl = facadeView.GetID;
      GUIControl.ShowControl(GetID, iControl);
      GUIControl.FocusControl(GetID, iControl);


      string strLine = String.Empty;
      View view = CurrentView;
      switch (view)
      {
        case View.List:
          strLine = GUILocalizeStrings.Get(101);
          break;
        case View.Icons:
          strLine = GUILocalizeStrings.Get(100);
          break;
        case View.LargeIcons:
          strLine = GUILocalizeStrings.Get(417);
          break;
        case View.FilmStrip:
          strLine = GUILocalizeStrings.Get(733);
          break;
      }
      GUIControl.SetControlLabel(GetID, btnViewAs.GetID, strLine);


      switch (CurrentSortMethod)
      {
        case VideoSort.SortMethod.Name:
          strLine = GUILocalizeStrings.Get(365);
          break;
        case VideoSort.SortMethod.Date:
          strLine = GUILocalizeStrings.Get(104);
          break;
        case VideoSort.SortMethod.Size:
          strLine = GUILocalizeStrings.Get(105);
          break;
        case VideoSort.SortMethod.Year:
          strLine = GUILocalizeStrings.Get(366);
          break;
        case VideoSort.SortMethod.Rating:
          strLine = GUILocalizeStrings.Get(367);
          break;
        case VideoSort.SortMethod.Label:
          strLine = GUILocalizeStrings.Get(430);
          break;
      }

      if (btnSortBy != null)
      {
        btnSortBy.Label = strLine;
        btnSortBy.IsAscending = CurrentSortAsc;
      }
    }

    protected virtual void OnClick(int item)
    {
    }

    protected virtual void OnQueueItem(int item)
    {
    }


    protected override void OnPageLoad()
    {
      GUIVideoOverlay videoOverlay = (GUIVideoOverlay)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_VIDEO_OVERLAY);
      if ((videoOverlay != null) && (videoOverlay.Focused))
        videoOverlay.Focused = false;

      LoadSettings();

      if (btnSortBy != null)
        btnSortBy.SortChanged += new SortEventHandler(SortChanged);
      base.OnPageLoad();
    }

    protected override void OnPageDestroy(int newWindowId)
    {
      SaveSettings();

      // Save view
      using (MediaPortal.Profile.Settings xmlwriter = new MediaPortal.Profile.Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml")))
      {
        xmlwriter.SetValue("movies", "startWindow", VideoState.StartWindow.ToString());
        xmlwriter.SetValue("movies", "startview", VideoState.View);
      }
      base.OnPageDestroy(newWindowId);
    }

    #region Sort Members
    protected virtual void OnSort()
    {
      SetLabels();
      facadeView.Sort(new VideoSort(CurrentSortMethod, CurrentSortAsc));
      UpdateButtonStates();
    }

    #endregion


    protected virtual void SetLabels()
    {
      for (int i = 0; i < facadeView.Count; ++i)
      {
        GUIListItem item = facadeView[i];
        IMDBMovie movie = item.AlbumInfoTag as IMDBMovie;

        if (movie != null && movie.ID > 0 && !item.IsFolder)
        {
          if (CurrentSortMethod == VideoSort.SortMethod.Name)
            item.Label2 = MediaPortal.Util.Utils.SecondsToHMString(movie.RunTime * 60);
          else if (CurrentSortMethod == VideoSort.SortMethod.Year)
            item.Label2 = movie.Year.ToString();
          else if (CurrentSortMethod == VideoSort.SortMethod.Rating)
            item.Label2 = movie.Rating.ToString();
          else if (CurrentSortMethod == VideoSort.SortMethod.Label)
            item.Label2 = movie.DVDLabel.ToString();
          else if (CurrentSortMethod == VideoSort.SortMethod.Size)
          {
            if (item.FileInfo != null)
              item.Label2 = MediaPortal.Util.Utils.GetSize(item.FileInfo.Length);
            else
              item.Label2 = MediaPortal.Util.Utils.SecondsToHMString(movie.RunTime * 60);
          }
        }
        else
        {
          string strSize1 = String.Empty, strDate = String.Empty;
          if (item.FileInfo != null && !item.IsFolder)
            strSize1 = MediaPortal.Util.Utils.GetSize(item.FileInfo.Length);
          if (item.FileInfo != null && !item.IsFolder)
            strDate = item.FileInfo.ModificationTime.ToShortDateString() + " " + item.FileInfo.ModificationTime.ToString("t", CultureInfo.CurrentCulture.DateTimeFormat);
          if (CurrentSortMethod == VideoSort.SortMethod.Name)
            item.Label2 = strSize1;
          else if (CurrentSortMethod == VideoSort.SortMethod.Date)
            item.Label2 = strDate;
          else
            item.Label2 = strSize1;
        }
      }
    }

    protected void SwitchView()
    {
      if (facadeView == null)
        return;
      switch (CurrentView)
      {
        case View.List:
          facadeView.View = GUIFacadeControl.ViewMode.List;
          break;
        case View.Icons:
          facadeView.View = GUIFacadeControl.ViewMode.SmallIcons;
          break;
        case View.LargeIcons:
          facadeView.View = GUIFacadeControl.ViewMode.LargeIcons;
          break;
        case View.FilmStrip:
          facadeView.View = GUIFacadeControl.ViewMode.Filmstrip;
          break;
      }
    }


    protected bool GetKeyboard(ref string strLine)
    {
      VirtualKeyboard keyboard = (VirtualKeyboard)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_VIRTUAL_KEYBOARD);
      if (null == keyboard)
        return false;
      keyboard.Reset();
      keyboard.Text = strLine;
      keyboard.DoModal(GetID);
      if (keyboard.IsConfirmed)
      {
        strLine = keyboard.Text;
        return true;
      }
      return false;
    }


    protected void OnShowViews()
    {
      GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
      if (dlg == null)
        return;
      dlg.Reset();
      dlg.SetHeading(499); // menu
      dlg.Add(GUILocalizeStrings.Get(134));//videos
      foreach (ViewDefinition view in handler.Views)
      {
        dlg.Add(view.LocalizedName); //play
      }
      dlg.DoModal(GetID);
      if (dlg.SelectedLabel == -1)
        return;
      if (dlg.SelectedLabel == 0)
      {
        int nNewWindow = (int)GUIWindow.Window.WINDOW_VIDEOS;
        VideoState.StartWindow = nNewWindow;
        if (nNewWindow != GetID)
        {
          GUIVideoFiles.Reset();
          GUIWindowManager.ReplaceWindow(nNewWindow);
        }
      }
      else
      {
        ViewDefinition selectedView = (ViewDefinition)handler.Views[dlg.SelectedLabel - 1];
        handler.CurrentView = selectedView.Name;
        VideoState.View = selectedView.Name;
        int nNewWindow = (int)GUIWindow.Window.WINDOW_VIDEO_TITLE;
        if (GetID != nNewWindow)
        {
          VideoState.StartWindow = nNewWindow;
          if (nNewWindow != GetID)
          {
            GUIWindowManager.ReplaceWindow(nNewWindow);
          }
        }
        else
        {
          LoadDirectory(String.Empty);
        }
      }
    }

    protected void OnShowSortOptions()
    {
      GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
      if (dlg == null)
        return;
      dlg.Reset();
      dlg.SetHeading(495);

      dlg.AddLocalizedString(365); // name
      dlg.AddLocalizedString(104); // date
      dlg.AddLocalizedString(105); // size
      dlg.AddLocalizedString(366); // year
      dlg.AddLocalizedString(367); // rating
      dlg.AddLocalizedString(430); // label

      dlg.DoModal(GetID);

      if (dlg.SelectedLabel == -1)
        return;

      switch (dlg.SelectedId)
      {
        case 365:
          CurrentSortMethod = VideoSort.SortMethod.Name;
          break;
        case 104:
          CurrentSortMethod = VideoSort.SortMethod.Date;
          break;
        case 105:
          CurrentSortMethod = VideoSort.SortMethod.Size;
          break;
        case 366:
          CurrentSortMethod = VideoSort.SortMethod.Year;
          break;
        case 367:
          CurrentSortMethod = VideoSort.SortMethod.Rating;
          break;
        case 430:
          CurrentSortMethod = VideoSort.SortMethod.Label;
          break;
        default:
          CurrentSortMethod = VideoSort.SortMethod.Name;
          break;
      }

      OnSort();
      GUIControl.FocusControl(GetID, btnSortBy.GetID);
    }

    protected virtual void LoadDirectory(string path)
    {
    }

    void OnInfoFile(GUIListItem item)
    {
    }

    void OnInfoFolder(GUIListItem item)
    {
    }

    protected virtual void OnInfo(int iItem)
    {
    }

    protected virtual void AddItemToPlayList(GUIListItem pItem)
    {
      if (!pItem.IsFolder)
      {
        //TODO
        if (MediaPortal.Util.Utils.IsVideo(pItem.Path) && !PlayListFactory.IsPlayList(pItem.Path))
        {
          PlayListItem playlistItem = new PlayListItem();
          playlistItem.Type = PlayListItem.PlayListItemType.Video;
          playlistItem.FileName = pItem.Path;
          playlistItem.Description = pItem.Label;
          playlistItem.Duration = pItem.Duration;
          playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_VIDEO).Add(playlistItem);
        }
      }
    }

    void SortChanged(object sender, SortEventArgs e)
    {
      CurrentSortAsc = e.Order != System.Windows.Forms.SortOrder.Descending;

      OnSort();
      //UpdateButtonStates();
      GUIControl.FocusControl(GetID, ((GUIControl)sender).GetID);
    }
  }
}