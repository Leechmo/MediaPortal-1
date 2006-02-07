/* 
 *	Copyright (C) 2005 Team MediaPortal
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
#define HW_PID_FILTERING
//#define DUMP
//#define USEMTSWRITER
#define COMPARE_PMT
#region usings
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DShowNET;
using DShowNET.Helper;
using DShowNET.MPSA;
using DShowNET.MPTSWriter;
using DirectShowLib;
using DirectShowLib.BDA;
using DirectShowLib.SBE;
using MediaPortal.Util;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.TV.Database;
using MediaPortal.TV.Epg;
using TVCapture;
using System.Xml;
//using DirectX.Capture;
using MediaPortal.Radio.Database;
using Toub.MediaCenter.Dvrms.Metadata;
using MediaPortal.TV.BDA;
#endregion

namespace MediaPortal.TV.Recording
{
  public class DVBGraphSkyStar2 : DVBGraphBDA
  {
    #region enums

    public enum TunerType
    {
      ttSat = 0,
      ttCable = 1,
      ttTerrestrial = 2,
      ttATSC = 3,
      ttUnknown = -1
    }
    protected enum eModulationTAG
    {
      QAM_4 = 2,
      QAM_16,
      QAM_32,
      QAM_64,
      QAM_128,
      QAM_256,
      MODE_UNKNOWN = -1
    }
    #endregion

    #region Structs
    /*
				*	Structure completedy by GetTunerCapabilities() to return tuner capabilities
				*/
    public struct tTunerCapabilities
    {
      public TunerType eModulation;
      public int dwConstellationSupported;       // Show if SetModulation() is supported
      public int dwFECSupported;                 // Show if SetFec() is suppoted
      public int dwMinTransponderFreqInKHz;
      public int dwMaxTransponderFreqInKHz;
      public int dwMinTunerFreqInKHz;
      public int dwMaxTunerFreqInKHz;
      public int dwMinSymbolRateInBaud;
      public int dwMaxSymbolRateInBaud;
      public int bAutoSymbolRate;				// Obsolte		
      public int dwPerformanceMonitoring;        // See bitmask definitions below
      public int dwLockTimeInMilliSecond;		// lock time in millisecond
      public int dwKernelLockTimeInMilliSecond;	// lock time for kernel
      public int dwAcquisitionCapabilities;
    }

    #endregion
    #region imports

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int SetPidToPin(DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 dataCtrl, UInt16 pin, UInt16 pid);

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool DeleteAllPIDs(DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 dataCtrl, UInt16 pin);

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetSNR(DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2 tunerCtrl, [Out] out int a, [Out] out int b);
    #endregion
    #region variables
    protected IBaseFilter _filterB2C2Adapter = null;
    protected DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 _interfaceB2C2DataCtrl = null;
    protected DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2 _interfaceB2C2TunerCtrl = null;
    protected DVBSkyStar2Helper.IB2C2MPEG2AVCtrl2 _interfaceB2C2AvcCtrl = null;
    string _cardType = "";
    string _cardFilename = "";
    bool _lastTuneFailed;
    #endregion

    public DVBGraphSkyStar2(TVCaptureDevice pCard)
      : base(pCard)
    {
      using (MediaPortal.Profile.Settings xmlreader = new MediaPortal.Profile.Settings("MediaPortal.xml"))
      {
        _cardType = xmlreader.GetValueAsString("DVBSS2", "cardtype", "");
        _cardFilename = xmlreader.GetValueAsString("dvb_ts_cards", "filename", "");
      }
      _streamDemuxer.SetCardType((int)DVBEPG.EPGCard.TechnisatStarCards, NetworkType.DVBS);
    }
    public override bool CreateGraph(int Quality)
    {
      try
      {
        _lastTuneFailed = true;
        _inScanningMode = false;
        //check if we didnt already create a graph
        if (_graphState != State.None)
          return false;
        _currentTuningObject = null;
        _isUsingAC3 = false;
        if (_streamDemuxer != null)
          _streamDemuxer.GrabTeletext(false);

        _isGraphRunning = false;
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph(). ");

        //no card defined? then we cannot build a graph
        if (_card == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:card is not defined");
          return false;
        }
        //create new instance of VMR9 helper utility
        _vmr9 = new VMR9Util();

        // Make a new filter graph
        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2:create new filter graph (IGraphBuilder)");
        _graphBuilder = (IGraphBuilder)new FilterGraph();


        // Get the Capture Graph Builder
        _captureGraphBuilderInterface = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2:Link the CaptureGraphBuilder to the filter graph (SetFiltergraph)");
        int hr = _captureGraphBuilderInterface.SetFiltergraph(_graphBuilder);
        if (hr < 0)
        {
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED link :0x{0:X}", hr);
          return false;
        }
        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2:Add graph to ROT table");
        _rotEntry = new DsROTEntry((IFilterGraph)_graphBuilder);


        //=========================================================================================================
        // add the Sample grabber (not in configuration.exe) 
        //=========================================================================================================
        _filterSampleGrabber = null;
        _sampleInterface = null;
        if (GUIGraphicsContext.DX9Device != null)
        {
          _filterSampleGrabber = (IBaseFilter)new SampleGrabber();
          _sampleInterface = (ISampleGrabber)_filterSampleGrabber;
          _graphBuilder.AddFilter(_filterSampleGrabber, "Sample Grabber");
        }

        //=========================================================================================================
        // add the MPEG-2 Demultiplexer 
        //=========================================================================================================
        // Use CLSID_filterMpeg2Demultiplexer to create the filter
        //Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() create MPEG2-Demultiplexer");
        _filterMpeg2Demultiplexer = (IBaseFilter)new MPEG2Demultiplexer();
        if (_filterMpeg2Demultiplexer == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to create Mpeg2 Demultiplexer");
          return false;
        }

        // Add the Demux to the graph
        //Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() add mpeg2 demuxer to graph");
        _graphBuilder.AddFilter(_filterMpeg2Demultiplexer, "MPEG-2 Demultiplexer");
        IMpeg2Demultiplexer demuxer = _filterMpeg2Demultiplexer as IMpeg2Demultiplexer;
        //=========================================================================================================
        // create PSI output pin on demuxer
        //=========================================================================================================

        AMMediaType mtSections = new AMMediaType();
        mtSections.majorType = MEDIATYPE_MPEG2_SECTIONS;
        mtSections.subType = MediaSubType.None;
        mtSections.formatType = FormatType.None;
        IPin pinSectionsOut;
        hr = demuxer.CreateOutputPin(mtSections, "sections", out pinSectionsOut);
        if (hr != 0 || pinSectionsOut == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSS2:FAILED to create sections pin:0x{0:X}", hr);
          return false;
        }

        //=========================================================================================================
        // add the stream analyzer
        //=========================================================================================================
        //Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() add stream analyzer");
        _filterDvbAnalyzer = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId.MPStreamAnalyzer, true));
        _analyzerInterface = (IStreamAnalyzer)_filterDvbAnalyzer;
        _epgGrabberInterface = _filterDvbAnalyzer as IEPGGrabber;
        _mhwGrabberInterface = _filterDvbAnalyzer as IMHWGrabber;
        _atscGrabberInterface = _filterDvbAnalyzer as IATSCGrabber;
        hr = _graphBuilder.AddFilter(_filterDvbAnalyzer, "Stream-Analyzer");
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2: FAILED to add SectionsFilter 0x{0:X}", hr);
          return false;
        }

        //=========================================================================================================
        // add the skystar 2 specific filters
        //=========================================================================================================
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() create B2C2 adapter");
        _filterB2C2Adapter = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(DVBSkyStar2Helper.CLSID_B2C2Adapter, false));
        if (_filterB2C2Adapter == null)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:creategraph() _filterB2C2Adapter not found");
          return false;
        }
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:creategraph() add filters to graph");
        hr = _graphBuilder.AddFilter(_filterB2C2Adapter, "B2C2-Source");
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: FAILED to add B2C2-Adapter");
          return false;
        }
        // get interfaces
        _interfaceB2C2DataCtrl = _filterB2C2Adapter as DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3;
        if (_interfaceB2C2DataCtrl == null)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: cannot get IB2C2MPEG2DataCtrl3");
          return false;
        }
        _interfaceB2C2TunerCtrl = _filterB2C2Adapter as DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2;
        if (_interfaceB2C2TunerCtrl == null)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: cannot get IB2C2MPEG2TunerCtrl2");
          return false;
        }
        _interfaceB2C2AvcCtrl = _filterB2C2Adapter as DVBSkyStar2Helper.IB2C2MPEG2AVCtrl2;
        if (_interfaceB2C2AvcCtrl == null)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: cannot get IB2C2MPEG2AVCtrl2");
          return false;
        }

        //=========================================================================================================
        // initialize skystar 2 tuner
        //=========================================================================================================
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: Initialize Tuner()");
        hr = _interfaceB2C2TunerCtrl.Initialize();
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: Tuner initialize failed:0x{0:X}", hr);
          return false;
        }
        // Get tuner type (DVBS, DVBC, DVBT, ATSC)

        tTunerCapabilities tc;
        int lTunerCapSize = Marshal.SizeOf(typeof(tTunerCapabilities));

        IntPtr ptCaps = Marshal.AllocHGlobal(lTunerCapSize);

        hr = _interfaceB2C2TunerCtrl.GetTunerCapabilities(ptCaps, ref lTunerCapSize);
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: Tuner Type failed:0x{0:X}", hr);
          return false;
        }

        tc = (tTunerCapabilities)Marshal.PtrToStructure(ptCaps, typeof(tTunerCapabilities));

        switch (tc.eModulation)
        {
          case TunerType.ttSat:
            _networkType = NetworkType.DVBS;
            break;
          case TunerType.ttCable:
            _networkType = NetworkType.DVBC;
            break;
          case TunerType.ttTerrestrial:
            _networkType = NetworkType.DVBT;
            break;
          case TunerType.ttATSC:
            _networkType = NetworkType.ATSC;
            break;
          case TunerType.ttUnknown:
            _networkType = NetworkType.Unknown;
            break;
        }
        Marshal.FreeHGlobal(ptCaps);

        // call checklock once, the return value dont matter

        hr = _interfaceB2C2TunerCtrl.CheckLock();
        //=========================================================================================================
        // connect B2BC-Source "Data 0" -> samplegrabber
        //=========================================================================================================
        
        if (GUIGraphicsContext.DX9Device != null && _sampleInterface != null)
        {
          IPin pinData0 = DsFindPin.ByDirection(_filterB2C2Adapter, PinDirection.Output, 2);
          if (pinData0 == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get pin 'Data 0' from B2BC source");
            return false;
          }

          IPin pinIn = DsFindPin.ByDirection(_filterSampleGrabber, PinDirection.Input, 0);
          if (pinIn == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get input pin from sample grabber");
            return false;
          }

          hr=_graphBuilder.Connect(pinData0, pinIn);
          if (hr!=0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to connect B2BC->sample grabber");
            return false;
          }
        }

        if (GUIGraphicsContext.DX9Device != null && _sampleInterface != null)
        {
          //Log.WriteFile(Log.LogType.Capture, "DVBGraphBDA:CreateGraph() connect grabber->demuxer");
          if (!ConnectFilters(ref _filterSampleGrabber, ref _filterMpeg2Demultiplexer))
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphBDA:Failed to connect samplegrabber filter->mpeg2 demultiplexer");
            return false;
          }
        }
        else
        {
          IPin pinData0 = DsFindPin.ByDirection(_filterB2C2Adapter, PinDirection.Output, 2);
          if (pinData0 == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get pin 'Data 0' from B2BC source");
            return false;
          }
          IPin pinIn = DsFindPin.ByDirection(_filterMpeg2Demultiplexer, PinDirection.Input, 0);
          if (pinIn == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get input pin from sample grabber");
            return false;
          }

          hr = _graphBuilder.Connect(pinData0, pinIn);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to connect B2BC->demuxer");
            return false;
          }
        }

        //=========================================================================================================
        // 1. connect demuxer->analyzer
        // 2. find audio/video output pins on demuxer
        //=========================================================================================================
        //        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() find audio/video pins");
        bool connected = false;
        IPin pinAnalyzerIn = DsFindPin.ByDirection(_filterDvbAnalyzer, PinDirection.Input, 0);
        IEnumPins pinEnum;
        _filterMpeg2Demultiplexer.EnumPins(out pinEnum);
        pinEnum.Reset();
        IPin[] pin = new IPin[1];
        int fetched = 0;
        while (pinEnum.Next(1, pin, out fetched) == 0)
        {
          if (fetched == 1)
          {
            IEnumMediaTypes enumMedia;
            pin[0].EnumMediaTypes(out enumMedia);
            enumMedia.Reset();
            DirectShowLib.AMMediaType[] pinMediaType = new DirectShowLib.AMMediaType[2];
            int fetchedm = 0;
            while (enumMedia.Next(1, pinMediaType, out fetchedm) == 0)
            {
              if (fetchedm == 1)
              {
                if (pinMediaType[0].majorType == MediaType.Audio)
                {
                  //Log.Write("DVBGraphSkyStar2: found audio pin");
                  _pinDemuxerAudio = pin[0];
                  break;
                }
                if (pinMediaType[0].majorType == MediaType.Video)
                {
                  //Log.Write("DVBGraphSkyStar2: found video pin");
                  _pinDemuxerVideo = pin[0];
                  break;
                }
                if (pinMediaType[0].majorType == MEDIATYPE_MPEG2_SECTIONS && !connected)
                {
                  IPin pinConnectedTo = null;
                  pin[0].ConnectedTo(out pinConnectedTo);
                  if (pinConnectedTo == null)
                  {
                    //Log.Write("DVBGraphSkyStar2:connect mpeg2 demux->stream analyzer");
                    hr = _graphBuilder.Connect(pin[0], pinAnalyzerIn);
                    if (hr == 0)
                    {
                      connected = true;
                      //Log.Write("DVBGraphSkyStar2:connected mpeg2 demux->stream analyzer");
                    }
                    else
                    {
                      Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to connect mpeg2 demux->stream analyzer");
                    }
                  }
                  if (pinConnectedTo != null)
                  {
                    Marshal.ReleaseComObject(pinConnectedTo);
                    pinConnectedTo = null;
                  }
                }
              }
            }
            Marshal.ReleaseComObject(enumMedia); enumMedia = null;
            Marshal.ReleaseComObject(pin[0]); pin[0] = null;
          }
        }
        Marshal.ReleaseComObject(pinEnum); pinEnum = null;
        if (pinAnalyzerIn != null) Marshal.ReleaseComObject(pinAnalyzerIn); pinAnalyzerIn = null;
        //get the video/audio output pins of the mpeg2 demultiplexer
        if (_pinDemuxerVideo == null)
        {
          //video pin not found
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get pin '{0}' (video out) from MPEG-2 Demultiplexer", _pinDemuxerVideo);
          return false;
        }
        if (_pinDemuxerAudio == null)
        {
          //audio pin not found
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:Failed to get pin '{0}' (audio out)  from MPEG-2 Demultiplexer", _pinDemuxerAudio);
          return false;
        }

        //=========================================================================================================
        // add the AC3 pin, mpeg1 audio pin to the MPEG2 demultiplexer
        //=========================================================================================================
        //Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:CreateGraph() create ac3/mpg1 pins");
        if (demuxer != null)
        {
          AMMediaType mpegVideoOut = new AMMediaType();
          mpegVideoOut.majorType = MediaType.Video;
          mpegVideoOut.subType = MediaSubType.Mpeg2Video;

          Size FrameSize = new Size(100, 100);
          mpegVideoOut.unkPtr = IntPtr.Zero;
          mpegVideoOut.sampleSize = 0;
          mpegVideoOut.temporalCompression = false;
          mpegVideoOut.fixedSizeSamples = true;

          //Mpeg2ProgramVideo=new byte[Mpeg2ProgramVideo.GetLength(0)];
          mpegVideoOut.formatType = FormatType.Mpeg2Video;
          mpegVideoOut.formatSize = Mpeg2ProgramVideo.GetLength(0);
          mpegVideoOut.formatPtr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(mpegVideoOut.formatSize);
          System.Runtime.InteropServices.Marshal.Copy(Mpeg2ProgramVideo, 0, mpegVideoOut.formatPtr, mpegVideoOut.formatSize);

          AMMediaType mpegAudioOut = new AMMediaType();
          mpegAudioOut.majorType = MediaType.Audio;
          mpegAudioOut.subType = MediaSubType.Mpeg2Audio;
          mpegAudioOut.sampleSize = 0;
          mpegAudioOut.temporalCompression = false;
          mpegAudioOut.fixedSizeSamples = true;
          mpegAudioOut.unkPtr = IntPtr.Zero;
          mpegAudioOut.formatType = FormatType.WaveEx;
          mpegAudioOut.formatSize = MPEG1AudioFormat.GetLength(0);
          mpegAudioOut.formatPtr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(mpegAudioOut.formatSize);
          System.Runtime.InteropServices.Marshal.Copy(MPEG1AudioFormat, 0, mpegAudioOut.formatPtr, mpegAudioOut.formatSize);
          hr = demuxer.CreateOutputPin(mpegAudioOut, "audio", out _pinDemuxerAudio);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2: FAILED to create audio output pin on demuxer");
            return false;
          }

          hr = demuxer.CreateOutputPin(mpegVideoOut/*vidOut*/, "video", out _pinDemuxerVideo);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2: FAILED to create video output pin on demuxer");
            return false;
          }

          //Log.WriteFile(Log.LogType.Capture, false, "mpeg2: create ac3 pin");
          AMMediaType mediaAC3 = new AMMediaType();
          mediaAC3.majorType = MediaType.Audio;
          mediaAC3.subType = MediaSubType.DolbyAC3;
          mediaAC3.sampleSize = 0;
          mediaAC3.temporalCompression = false;
          mediaAC3.fixedSizeSamples = false;
          mediaAC3.unkPtr = IntPtr.Zero;
          mediaAC3.formatType = FormatType.WaveEx;
          mediaAC3.formatSize = MPEG1AudioFormat.GetLength(0);
          mediaAC3.formatPtr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(mediaAC3.formatSize);
          System.Runtime.InteropServices.Marshal.Copy(MPEG1AudioFormat, 0, mediaAC3.formatPtr, mediaAC3.formatSize);

          hr = demuxer.CreateOutputPin(mediaAC3/*vidOut*/, "AC3", out _pinAC3Out);
          if (hr != 0 || _pinAC3Out == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to create AC3 pin:0x{0:X}", hr);
          }

          //Log.WriteFile(Log.LogType.Capture, false, "DVBGraphSkyStar2: create mpg1 audio pin");
          AMMediaType mediaMPG1 = new AMMediaType();
          mediaMPG1.majorType = MediaType.Audio;
          mediaMPG1.subType = MediaSubType.MPEG1AudioPayload;
          mediaMPG1.sampleSize = 0;
          mediaMPG1.temporalCompression = false;
          mediaMPG1.fixedSizeSamples = false;
          mediaMPG1.unkPtr = IntPtr.Zero;
          mediaMPG1.formatType = FormatType.WaveEx;
          mediaMPG1.formatSize = MPEG1AudioFormat.GetLength(0);
          mediaMPG1.formatPtr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(mediaMPG1.formatSize);
          System.Runtime.InteropServices.Marshal.Copy(MPEG1AudioFormat, 0, mediaMPG1.formatPtr, mediaMPG1.formatSize);

          hr = demuxer.CreateOutputPin(mediaMPG1/*vidOut*/, "audioMpg1", out _pinMPG1Out);
          if (hr != 0 || _pinMPG1Out == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to create MPG1 pin:0x{0:X}", hr);
          }

          //=========================================================================================================
          // add the EPG/MHW pin to the MPEG2 demultiplexer
          //=========================================================================================================
          //create EPG pins
          //Log.Write("DVBGraphSkyStar2:Create EPG pin");
          AMMediaType mtEPG = new AMMediaType();
          mtEPG.majorType = MEDIATYPE_MPEG2_SECTIONS;
          mtEPG.subType = MediaSubType.None;
          mtEPG.formatType = FormatType.None;

          IPin pinEPGout, pinMHW1Out, pinMHW2Out;
          hr = demuxer.CreateOutputPin(mtEPG, "EPG", out pinEPGout);
          if (hr != 0 || pinEPGout == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to create EPG pin:0x{0:X}", hr);
            return false;
          }
          hr = demuxer.CreateOutputPin(mtEPG, "MHW1", out pinMHW1Out);
          if (hr != 0 || pinMHW1Out == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to create MHW1 pin:0x{0:X}", hr);
            return false;
          }
          hr = demuxer.CreateOutputPin(mtEPG, "MHW2", out pinMHW2Out);
          if (hr != 0 || pinMHW2Out == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to create MHW2 pin:0x{0:X}", hr);
            return false;
          }

          //Log.Write("DVBGraphSkyStar2:Get EPGs pin of analyzer");
          IPin pinMHW1In = DsFindPin.ByDirection(_filterDvbAnalyzer, PinDirection.Input, 1);
          if (pinMHW1In == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to get MHW1 pin on MSPA");
            return false;
          }
          IPin pinMHW2In = DsFindPin.ByDirection(_filterDvbAnalyzer, PinDirection.Input, 2);
          if (pinMHW2In == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to get MHW2 pin on MSPA");
            return false;
          }
          IPin pinEPGIn = DsFindPin.ByDirection(_filterDvbAnalyzer, PinDirection.Input, 3);
          if (pinEPGIn == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to get EPG pin on MSPA");
            return false;
          }

          //Log.Write("DVBGraphSkyStar2:Connect epg pins");
          hr = _graphBuilder.Connect(pinEPGout, pinEPGIn);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to connect EPG pin:0x{0:X}", hr);
            return false;
          }
          hr = _graphBuilder.Connect(pinMHW1Out, pinMHW1In);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to connect MHW1 pin:0x{0:X}", hr);
            return false;
          }
          hr = _graphBuilder.Connect(pinMHW2Out, pinMHW2In);
          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:FAILED to connect MHW2 pin:0x{0:X}", hr);
            return false;
          }
          //Log.Write("DVBGraphSkyStar2:Demuxer is setup");

          if (pinEPGout != null) Marshal.ReleaseComObject(pinEPGout); pinEPGout = null;
          if (pinMHW1Out != null) Marshal.ReleaseComObject(pinMHW1Out); pinMHW1Out = null;
          if (pinMHW2Out != null) Marshal.ReleaseComObject(pinMHW2Out); pinMHW2Out = null;
          if (pinMHW1In != null) Marshal.ReleaseComObject(pinMHW1In); pinMHW1In = null;
          if (pinMHW2In != null) Marshal.ReleaseComObject(pinMHW2In); pinMHW2In = null;
          if (pinEPGIn != null) Marshal.ReleaseComObject(pinEPGIn); pinEPGIn = null;
        }
        else
          Log.WriteFile(Log.LogType.Capture, true, "DVBGraphSkyStar2:mapped IMPEG2Demultiplexer not found");

        //=========================================================================================================
        // Create the streambuffer engine and mpeg2 video analyzer components since we need them for
        // recording and timeshifting
        //=========================================================================================================
        m_StreamBufferSink = new StreamBufferSink();
        m_mpeg2Analyzer = new VideoAnalyzer();
        m_IStreamBufferSink = (IStreamBufferSink3)m_StreamBufferSink;
        _graphState = State.Created;

        GetTunerSignalStatistics();
        if (_tunerStatistics.Count == 0)
        {
          Log.Write("DVBGraphSkyStar2:Failed to get tuner statistics");
        }
        Log.Write("DVBGraphSkyStar2:got {0} tuner statistics", _tunerStatistics.Count);


        //_streamDemuxer.OnAudioFormatChanged+=new MediaPortal.TV.Recording.DVBDemuxer.OnAudioChanged(m_streamDemuxer_OnAudioFormatChanged);
        //_streamDemuxer.OnPMTIsChanged+=new MediaPortal.TV.Recording.DVBDemuxer.OnPMTChanged(m_streamDemuxer_OnPMTIsChanged);
        _streamDemuxer.SetCardType((int)DVBEPG.EPGCard.BDACards, Network());
        //_streamDemuxer.OnGotTable+=new MediaPortal.TV.Recording.DVBDemuxer.OnTableReceived(m_streamDemuxer_OnGotTable);

        if (_sampleInterface != null)
        {
          AMMediaType mt = new AMMediaType();
          mt.majorType = MediaType.Stream;
          mt.subType = MediaSubTypeEx.MPEG2Transport;
          _sampleInterface.SetCallback(_streamDemuxer, 1);
          _sampleInterface.SetMediaType(mt);
          _sampleInterface.SetBufferSamples(false);
        }

        if (Network() == NetworkType.ATSC)
          _analyzerInterface.UseATSC(1);
        else
          _analyzerInterface.UseATSC(0);

        _epgGrabber.EPGInterface = _epgGrabberInterface;
        _epgGrabber.MHWInterface = _mhwGrabberInterface;
        _epgGrabber.ATSCInterface = _atscGrabberInterface;
        _epgGrabber.AnalyzerInterface = _analyzerInterface;
        _epgGrabber.Network = Network();
      }
      catch (Exception ex)
      {
        Log.Write(ex);
        return false;
      }
      return true;
    }

    public override void DeleteGraph()
    {
      try
      {
        if (_graphState < State.Created)
          return;
        int hr;
        _currentTuningObject = null;
        Log.Write("DVBGraphSkyStar2:DeleteGraph(). ac3=false");
        _isUsingAC3 = false;

        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:DeleteGraph()");
        StopRecording();
        StopTimeShifting();
        StopViewing();
        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: free tuner interfaces");

        // to clear buffers for epg and teletext
        if (_streamDemuxer != null)
        {
          _streamDemuxer.GrabTeletext(false);
          _streamDemuxer.SetChannelData(0, 0, 0, 0, 0, "", 0, 0);
        }

        //Log.Write("DVBGraphSkyStar2:stop graph");
        if (_mediaControl != null) _mediaControl.Stop();
        _mediaControl = null;
        //Log.Write("DVBGraphSkyStar2:graph stopped");

        if (_vmr9 != null)
        {
          //Log.Write("DVBGraphSkyStar2:remove vmr9");
          _vmr9.Dispose();
          _vmr9 = null;
        }


        if (m_recorderId >= 0)
        {
          DvrMsStop(m_recorderId);
          m_recorderId = -1;
        }

        _isGraphRunning = false;
        _basicVideoInterFace = null;
        _analyzerInterface = null;
        _epgGrabberInterface = null;
        _mhwGrabberInterface = null;
#if USEMTSWRITER
				_tsWriterInterface=null;
				_tsRecordInterface=null;
#endif
        //Log.Write("free pins");

        if (_pinDemuxerSections != null)
          Marshal.ReleaseComObject(_pinDemuxerSections);
        _pinDemuxerSections = null;

        if (_pinAC3Out != null)
          Marshal.ReleaseComObject(_pinAC3Out);
        _pinAC3Out = null;

        if (_pinMPG1Out != null)
          Marshal.ReleaseComObject(_pinMPG1Out);
        _pinMPG1Out = null;

        if (_pinDemuxerVideo != null)
          Marshal.ReleaseComObject(_pinDemuxerVideo);
        _pinDemuxerVideo = null;

        if (_pinDemuxerAudio != null)
          Marshal.ReleaseComObject(_pinDemuxerAudio);
        _pinDemuxerAudio = null;


        if (_filterB2C2Adapter != null)
        {
          while ((hr = Marshal.ReleaseComObject(_filterB2C2Adapter)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_filterB2C2Adapter):{0}", hr);
          _filterB2C2Adapter = null;
        }

        if (_filterDvbAnalyzer != null)
        {
          //Log.Write("free dvbanalyzer");
          while ((hr = Marshal.ReleaseComObject(_filterDvbAnalyzer)) > 0) ;
          if (hr != 0) Log.Write("ReleaseComObject(_filterDvbAnalyzer):{0}", hr);
          _filterDvbAnalyzer = null;
        }
#if USEMTSWRITER
				if (_filterTsWriter!=null)
				{
					Log.Write("free MPTSWriter");
					hr=Marshal.ReleaseComObject(_filterTsWriter);
					if (hr!=0) Log.Write("ReleaseComObject(_filterTsWriter):{0}",hr);
					_filterTsWriter=null;
				}
#endif
        if (_filterSmartTee != null)
        {
          while ((hr = Marshal.ReleaseComObject(_filterSmartTee)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_filterSmartTee):{0}", hr);
          _filterSmartTee = null;
        }

        if (_videoWindowInterface != null)
        {
          //Log.Write("DVBGraphSkyStar2:hide window");
          //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: hide video window");
          _videoWindowInterface.put_Visible(OABool.False);
          //_videoWindowInterface.put_Owner(IntPtr.Zero);
          _videoWindowInterface = null;
        }

        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: free other interfaces");
        _sampleInterface = null;
        if (_filterSampleGrabber != null)
        {
          //Log.Write("DVBGraphSkyStar2:free samplegrabber");
          while ((hr = Marshal.ReleaseComObject(_filterSampleGrabber)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_filterSampleGrabber):{0}", hr);
          _filterSampleGrabber = null;
        }


        if (m_IStreamBufferConfig != null)
        {
          while ((hr = Marshal.ReleaseComObject(m_IStreamBufferConfig)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(m_IStreamBufferConfig):{0}", hr);
          m_IStreamBufferConfig = null;
        }

        if (m_IStreamBufferSink != null)
        {
          while ((hr = Marshal.ReleaseComObject(m_IStreamBufferSink)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(m_IStreamBufferSink):{0}", hr);
          m_IStreamBufferSink = null;
        }

        if (m_StreamBufferSink != null)
        {
          //Log.Write("DVBGraphSkyStar2:free streambuffersink");
          while ((hr = Marshal.ReleaseComObject(m_StreamBufferSink)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(m_StreamBufferSink):{0}", hr);
          m_StreamBufferSink = null;
        }


        if (m_StreamBufferConfig != null)
        {
          //Log.Write("DVBGraphSkyStar2:free streambufferconfig");
          while ((hr = Marshal.ReleaseComObject(m_StreamBufferConfig)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(m_StreamBufferConfig):{0}", hr);
          m_StreamBufferConfig = null;
        }

        if (_filterMpeg2Demultiplexer != null)
        {
          //Log.Write("DVBGraphSkyStar2:free demux");
          while ((hr = Marshal.ReleaseComObject(_filterMpeg2Demultiplexer)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_filterMpeg2Demultiplexer):{0}", hr);
          _filterMpeg2Demultiplexer = null;
        }

        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: remove filters");

        if (_graphBuilder != null)
          DirectShowUtil.RemoveFilters(_graphBuilder);


        //Log.Write("DVBGraphSkyStar2:free remove graph");
        if (_rotEntry != null)
        {
          _rotEntry.Dispose();
        }
        _rotEntry = null;
        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: remove graph");
        if (_captureGraphBuilderInterface != null)
        {
          //Log.Write("DVBGraphSkyStar2:free remove capturegraphbuilder");
          while ((hr = Marshal.ReleaseComObject(_captureGraphBuilderInterface)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_captureGraphBuilderInterface):{0}", hr);
          _captureGraphBuilderInterface = null;
        }

        if (_graphBuilder != null)
        {
          //Log.Write("DVBGraphSkyStar2:free graphbuilder");
          while ((hr = Marshal.ReleaseComObject(_graphBuilder)) > 0) ;
          if (hr != 0) Log.Write("DVBGraphSkyStar2:ReleaseComObject(_graphBuilder):{0}", hr);
          _graphBuilder = null;
        }

#if DUMP
				if (fileout!=null)
				{
					fileout.Close();
					fileout=null;
				}
#endif

        GC.Collect(); GC.Collect(); GC.Collect();
        _graphState = State.None;
        //Log.WriteFile(Log.LogType.Capture,"DVBGraphSkyStar2: delete graph done");
      }
      catch (Exception ex)
      {
        Log.Write(ex);
      }
    }


    protected override void UpdateSignalPresent()
    {
      _signalPresent = (_lastTuneFailed == true ? false : true);
      _signalLevel = 100;
      if (_graphState == State.None) return;
      if (_interfaceB2C2TunerCtrl == null) return;
      int level;
      int quality;
      GetSNR(_interfaceB2C2TunerCtrl, out level, out quality);
      _signalQuality = quality;
    }

    public override NetworkType Network()
    {
      return _networkType;
    }


    protected override void SubmitTuneRequest(DVBChannel ch)
    {
      if (Tune(ch.Frequency, ch.Symbolrate, 6, ch.Polarity, ch.LNBKHz, ch.DiSEqC, ch.AudioPid, ch.VideoPid, ch.LNBFrequency, ch.ECMPid, ch.TeletextPid, ch.PMTPid, ch.PCRPid, ch.AudioLanguage3, ch.Audio3, ch.ProgramNumber, ch) == false)
      {
        _lastTuneFailed = true;
        return;
      }
      else
      {
        _lastTuneFailed = false;
      }
      SetHardwarePidFiltering();
      _processTimer = DateTime.MinValue;
      _pmtSendCounter = 0;
      UpdateSignalPresent();
      Log.Write("DVBGraphSkyStar2: signal strength:{0} signal quality:{1} signal present:{2}", SignalStrength(), SignalQuality(), SignalPresent());

    }

    private bool Tune(int Frequency, int SymbolRate, int FEC, int POL, int LNBKhz, int Diseq, int AudioPID, int VideoPID, int LNBFreq, int ecmPID, int ttxtPID, int pmtPID, int pcrPID, string pidText, int dvbsubPID, int programNumber, DVBChannel ch)
    {

      Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2: Tune() freq:{0} SR:{1} FEC:{2} POL:{3} LNBKhz:{4} Diseq:{5} audiopid:{6:X} videopid:{7:X} LNBFreq:{8} ecmPid:{9:X} pmtPid:{10:X} pcrPid{11:X}",
                    Frequency, SymbolRate, FEC, POL, LNBKhz, Diseq, AudioPID, VideoPID, LNBFreq, ecmPID, pmtPID, pcrPID);
      int hr = 0;				// the result
      int modulation = 5;		//QAM_64
      int guardinterval = 4;	//GUARD_INTERVAL_AUTO
      VideoRendererStatistics.VideoState = VideoRendererStatistics.State.VideoPresent;

      _lastTuneFailed = false;
      // clear epg
      if (Frequency > 13000)
        Frequency /= 1000;

      if (_interfaceB2C2TunerCtrl == null || _interfaceB2C2DataCtrl == null || _filterB2C2Adapter == null || _interfaceB2C2AvcCtrl == null)
        return false;

      // skystar
      hr = _interfaceB2C2TunerCtrl.SetFrequency(Frequency);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetFrequency:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      hr = _interfaceB2C2TunerCtrl.SetSymbolRate(SymbolRate);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetSymbolRate:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }

      hr = _interfaceB2C2TunerCtrl.SetLnbFrequency(LNBFreq);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetLnbFrequency:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      hr = _interfaceB2C2TunerCtrl.SetFec(FEC);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetFec:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      hr = _interfaceB2C2TunerCtrl.SetPolarity(POL);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetPolarity:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      hr = _interfaceB2C2TunerCtrl.SetLnbKHz(LNBKhz);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetLnbKHz:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      hr = _interfaceB2C2TunerCtrl.SetDiseqc(Diseq);
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetDiseqc:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
      }
      // cablestar
      if (_networkType == NetworkType.DVBC)
      {
        switch (ch.Modulation)
        {
          case (int)TunerLib.ModulationType.BDA_MOD_16QAM:
            modulation = (int)eModulationTAG.QAM_16;
            break;
          case (int)TunerLib.ModulationType.BDA_MOD_32QAM:
            modulation = (int)eModulationTAG.QAM_32;
            break;
          case (int)TunerLib.ModulationType.BDA_MOD_64QAM:
            modulation = (int)eModulationTAG.QAM_64;
            break;
          case (int)TunerLib.ModulationType.BDA_MOD_128QAM:
            modulation = (int)eModulationTAG.QAM_128;
            break;
          case (int)TunerLib.ModulationType.BDA_MOD_256QAM:
            modulation = (int)eModulationTAG.QAM_256;
            break;
        }
        hr = _interfaceB2C2TunerCtrl.SetModulation(modulation);
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetModulation:0x{0:X}", hr);
          return false;	// *** FUNCTION EXIT POINT
        }
      }

      // airstar
      if (_networkType == NetworkType.DVBT)
      {
        hr = _interfaceB2C2TunerCtrl.SetGuardInterval(guardinterval);
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetGuardInterval:0x{0:X}", hr);
          return false;	// *** FUNCTION EXIT POINT
        }
        // Set Channel Bandwidth (NOTE: Temporarily use polarity function to avoid having to 
        // change SDK interface for SetBandwidth)
        // from Technisat SDK 02/2005
        hr = _interfaceB2C2TunerCtrl.SetPolarity(ch.Bandwidth);
        if (hr != 0)
        {
          Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetBandwidth:0x{0:X}", hr);
          return false;	// *** FUNCTION EXIT POINT
        }
      }

      // final
      hr = _interfaceB2C2TunerCtrl.SetTunerStatus();
      if (hr != 0)
      {
        Log.WriteFile(Log.LogType.Capture, "DVBGraphSkyStar2:Tune for SkyStar2 FAILED: on SetTunerStatus:0x{0:X}", hr);
        return false;	// *** FUNCTION EXIT POINT
        //
      }
      return true;
    }

    protected override void SendHWPids(ArrayList pids)
    {
      DeleteAllPIDs(_interfaceB2C2DataCtrl, 0);
      if (pids.Count == 0)
      {
        SetPidToPin(_interfaceB2C2DataCtrl, 0, 0x2000);
      }
      else
      {
        foreach (ushort pid in pids)
        {
          SetPidToPin(_interfaceB2C2DataCtrl, 0, pid);
        }
      }
    }
  }
}
