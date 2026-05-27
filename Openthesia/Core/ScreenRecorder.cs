using Openthesia.Core.Midi;
using Openthesia.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.IO;

namespace Openthesia.Core;

public static class ScreenRecorder
{
    private static object? Recording { get; set; }
    public static object? Status { get; set; }
    public static bool IsScreenRecorderAvailable { get; private set; } = false;
    private static string _lastError = string.Empty;
    private static Type? _recorderType;
    private static Type? _recorderOptionsType;
    private static Type? _audioOptionsType;
    private static Type? _sourceOptionsType;
    private static Type? _videoEncoderOptionsType;
    private static Type? _windowRecordingSourceType;

    /// <summary>
    /// Initialisiert und prüft, ob die ScreenRecorder-Bibliothek verfügbar ist
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // Versuche die ScreenRecorderLib Assembly zu laden
            var assembly = System.Reflection.Assembly.Load("ScreenRecorderLib");
            
            // Lade die benötigten Typen
            _recorderType = assembly.GetType("ScreenRecorderLib.Recorder");
            _recorderOptionsType = assembly.GetType("ScreenRecorderLib.RecorderOptions");
            _audioOptionsType = assembly.GetType("ScreenRecorderLib.AudioOptions");
            _sourceOptionsType = assembly.GetType("ScreenRecorderLib.SourceOptions");
            _videoEncoderOptionsType = assembly.GetType("ScreenRecorderLib.VideoEncoderOptions");
            _windowRecordingSourceType = assembly.GetType("ScreenRecorderLib.WindowRecordingSource");
            
            if (_recorderType != null && _recorderOptionsType != null)
            {
                IsScreenRecorderAvailable = true;
                Console.WriteLine("ScreenRecorder erfolgreich geladen.");
            }
            else
            {
                IsScreenRecorderAvailable = false;
                _lastError = "Erforderliche Typen konnten nicht gefunden werden.";
            }
        }
        catch (System.IO.FileNotFoundException)
        {
            IsScreenRecorderAvailable = false;
            _lastError = "ScreenRecorderLib.dll wurde nicht gefunden. Video-Aufnahme ist nicht verfügbar.";
            Console.WriteLine($"ScreenRecorder nicht verfügbar: {_lastError}");
        }
        catch (Exception ex)
        {
            IsScreenRecorderAvailable = false;
            _lastError = ex.Message;
            Console.WriteLine($"ScreenRecorder nicht verfügbar: {ex.Message}");
        }
    }

    public static void StartRecording()
    {
        if (!IsScreenRecorderAvailable)
        {
            User32.MessageBox(IntPtr.Zero, 
                $"Screen Recorder ist nicht verfügbar.\n\nFehler: {_lastError}\n\nDie Video-Aufnahme Funktion ist deaktiviert.", 
                "Screen Recorder nicht verfügbar", 
                User32.MB_FLAGS.MB_ICONWARNING | User32.MB_FLAGS.MB_TOPMOST);
            return;
        }

        try
        {
            string fileName = MidiFileData.FileName.Replace(".mid", string.Empty);
            string date = DateTime.Now.ToString().Replace("/", "-").Replace(':', '.');
            string videoPath = Path.Combine(CoreSettings.VideoRecDestFolder, $"{fileName} {date}.mp4");

            // Erstelle WindowRecordingSource per Reflection
            var windowSource = Activator.CreateInstance(_windowRecordingSourceType!, Program._window.Handle);
            var sourcesList = Activator.CreateInstance(typeof(List<>).MakeGenericType(_windowRecordingSourceType!.BaseType!))!;
            sourcesList.GetType().GetMethod("Add")!.Invoke(sourcesList, new[] { windowSource });

            // Erstelle AudioOptions
            var audioOptions = Activator.CreateInstance(_audioOptionsType!);
            _audioOptionsType!.GetProperty("IsAudioEnabled")!.SetValue(audioOptions, true);
            _audioOptionsType!.GetProperty("IsOutputDeviceEnabled")!.SetValue(audioOptions, true);

            // Erstelle SourceOptions
            var sourceOptions = Activator.CreateInstance(_sourceOptionsType!);
            _sourceOptionsType!.GetProperty("RecordingSources")!.SetValue(sourceOptions, sourcesList);

            // Erstelle VideoEncoderOptions
            var videoEncoderOptions = Activator.CreateInstance(_videoEncoderOptionsType!);
            _videoEncoderOptionsType!.GetProperty("Framerate")!.SetValue(videoEncoderOptions, CoreSettings.VideoRecFramerate);

            // Erstelle RecorderOptions
            var options = Activator.CreateInstance(_recorderOptionsType!);
            _recorderOptionsType!.GetProperty("AudioOptions")!.SetValue(options, audioOptions);
            _recorderOptionsType!.GetProperty("SourceOptions")!.SetValue(options, sourceOptions);
            _recorderOptionsType!.GetProperty("VideoEncoderOptions")!.SetValue(options, videoEncoderOptions);

            // Erstelle Recorder
            var createRecorderMethod = _recorderType!.GetMethod("CreateRecorder", new[] { _recorderOptionsType });
            Recording = createRecorderMethod!.Invoke(null, new[] { options });

            // Registriere Events
            var onRecordingCompleteEvent = _recorderType!.GetEvent("OnRecordingComplete");
            var onRecordingFailedEvent = _recorderType!.GetEvent("OnRecordingFailed");
            var onStatusChangedEvent = _recorderType!.GetEvent("OnStatusChanged");

            var completeDelegate = Delegate.CreateDelegate(
                onRecordingCompleteEvent!.EventHandlerType!,
                typeof(ScreenRecorder).GetMethod(nameof(OnRecordingComplete), 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
            onRecordingCompleteEvent.AddEventHandler(Recording, completeDelegate);

            var failedDelegate = Delegate.CreateDelegate(
                onRecordingFailedEvent!.EventHandlerType!,
                typeof(ScreenRecorder).GetMethod(nameof(OnRecordingFailed), 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
            onRecordingFailedEvent.AddEventHandler(Recording, failedDelegate);

            var statusDelegate = Delegate.CreateDelegate(
                onStatusChangedEvent!.EventHandlerType!,
                typeof(ScreenRecorder).GetMethod(nameof(OnStatusChanged), 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
            onStatusChangedEvent.AddEventHandler(Recording, statusDelegate);

            // Starte Recording
            var recordMethod = _recorderType!.GetMethod("Record", new[] { typeof(string) });
            recordMethod!.Invoke(Recording, new object[] { videoPath });
        }
        catch (Exception ex)
        {
            IsScreenRecorderAvailable = false;
            _lastError = ex.Message;
            User32.MessageBox(IntPtr.Zero, 
                $"Screen Recorder konnte nicht gestartet werden:\n{ex.Message}", 
                "Recording Fehler", 
                User32.MB_FLAGS.MB_ICONERROR | User32.MB_FLAGS.MB_TOPMOST);
        }
    }

    public static void EndRecording()
    {
        if (!IsScreenRecorderAvailable || Recording == null)
            return;

        try
        {
            var stopMethod = _recorderType!.GetMethod("Stop");
            stopMethod!.Invoke(Recording, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Beenden der Aufnahme: {ex.Message}");
        }
    }

    /// <summary>
    /// Prüft, ob aktuell eine Aufnahme läuft
    /// </summary>
    public static bool IsRecording()
    {
        if (!IsScreenRecorderAvailable || Status == null)
            return false;

        try
        {
            // Verwende Reflection, um den Status-Wert zu prüfen
            var statusValue = Status.ToString();
            return statusValue == "Recording";
        }
        catch
        {
            return false;
        }
    }

    private static void OnRecordingComplete(object? sender, object? e)
    {
        Console.WriteLine("Recording completed");
        
        if (CoreSettings.VideoRecAutoPlay)
        {
            try
            {
                var recCompleteArgs = e!.GetType().GetProperty("FilePath")!.GetValue(e) as string;
                if (!string.IsNullOrEmpty(recCompleteArgs))
                {
                    Process.Start(new ProcessStartInfo(recCompleteArgs) { UseShellExecute = true });
                }
            }
            catch { }
        }

        if (CoreSettings.VideoRecOpenDestFolder)
        {
            try
            {
                Process.Start(new ProcessStartInfo(CoreSettings.VideoRecDestFolder) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private static void OnRecordingFailed(object? sender, object? e)
    {
        try
        {
            var error = e!.GetType().GetProperty("Error")!.GetValue(e)?.ToString() ?? "Unbekannter Fehler";
            Console.WriteLine($"Recording failed: {error}");
            User32.MessageBox(IntPtr.Zero, 
                $"Recording failed:\n{error}", 
                "Recording Error", 
                User32.MB_FLAGS.MB_ICONERROR | User32.MB_FLAGS.MB_TOPMOST);
        }
        catch { }
    }

    private static void OnStatusChanged(object? sender, object? e)
    {
        try
        {
            Status = e!.GetType().GetProperty("Status")!.GetValue(e);
            Console.WriteLine($"Recording status: {Status}");
        }
        catch { }
    }
}
