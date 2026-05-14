using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace android_shortcut_maker;

/// <summary>
/// Coordinates audio between multiple simultaneously running ASM instances
/// using a named shared memory block.
///
/// Layout: up to 16 slots, each slot is:
///   int  ScrcpyPid   (4 bytes)
///   byte AudioActive (1 byte, 1 = active, 0 = muted)
///   pad  (3 bytes)
/// Total: 128 bytes.
/// </summary>
internal static class InstanceAudioCoordinator
{
    private const string MapName    = "ASM_AudioCoordinator_v1";
    private const int    MaxSlots   = 16;
    private const int    SlotSize   = 8;   // int pid + byte active + 3 pad
    private const int    TotalSize  = MaxSlots * SlotSize;

    private static MemoryMappedFile?       _mmf;
    private static MemoryMappedViewAccessor? _accessor;
    private static int                     _mySlot = -1;
    private static readonly object         Lock    = new();

    // ── Registration ────────────────────────────────────────────────────────

    /// <summary>
    /// Register this instance. Call once from ShortcutHost.Start().
    /// audioActive should be true if this instance is starting with audio.
    /// </summary>
    public static void Register(int scrcpyPid, bool audioActive)
    {
        lock (Lock)
        {
            EnsureOpen();
            if (_accessor == null) return;

            // Find a free slot (pid == 0).
            for (var i = 0; i < MaxSlots; i++)
            {
                var pid = _accessor.ReadInt32(i * SlotSize);
                if (pid != 0) continue;

                _mySlot = i;
                WriteSlot(i, scrcpyPid, audioActive);
                Debugger.Show($"[AudioCoord] Registered at slot {i} pid={scrcpyPid} active={audioActive}");
                return;
            }

            Debugger.Show("[AudioCoord] No free slot found (max instances reached).");
        }
    }

    /// <summary>
    /// Unregister this instance. Call from ShortcutHost cleanup.
    /// </summary>
    public static void Unregister()
    {
        lock (Lock)
        {
            if (_mySlot < 0 || _accessor == null) return;
            WriteSlot(_mySlot, 0, false);
            Debugger.Show($"[AudioCoord] Unregistered slot {_mySlot}");
            _mySlot = -1;
        }
    }

    /// <summary>
    /// Update this instance's scrcpy PID (e.g. after a scrcpy restart).
    /// </summary>
    public static void UpdatePid(int newPid)
    {
        lock (Lock)
        {
            if (_mySlot < 0 || _accessor == null) return;
            var active = _accessor.ReadByte(_mySlot * SlotSize + 4) == 1;
            WriteSlot(_mySlot, newPid, active);
            Debugger.Show($"[AudioCoord] Updated slot {_mySlot} pid={newPid}");
        }
    }

    // ── Audio control ────────────────────────────────────────────────────────

    /// <summary>
    /// Mute all other instances and mark this one as audio-active.
    /// Call when this instance's audio is being activated.
    /// </summary>
    public static void ClaimAudio(int myScrcpyPid)
    {
        lock (Lock)
        {
            EnsureOpen();
            if (_accessor == null) return;

            for (var i = 0; i < MaxSlots; i++)
            {
                var pid    = _accessor.ReadInt32(i * SlotSize);
                var active = _accessor.ReadByte(i * SlotSize + 4) == 1;

                if (pid == 0) continue;

                if (i == _mySlot)
                {
                    // Mark ourselves as active.
                    WriteSlot(i, pid, true);
                }
                else if (active)
                {
                    // Mute the other instance's audio session.
                    Debugger.Show($"[AudioCoord] Muting other instance pid={pid}");
                    ScrcpyVolumeController.TrySetVolume(pid, 0f);
                    WriteSlot(i, pid, false);
                }
            }
        }
    }

    /// <summary>
    /// Mark this instance as audio-inactive (muted itself).
    /// Does not affect other instances.
    /// </summary>
    public static void ReleaseAudio(int myScrcpyPid)
    {
        lock (Lock)
        {
            if (_mySlot < 0 || _accessor == null) return;
            WriteSlot(_mySlot, myScrcpyPid, false);
            Debugger.Show($"[AudioCoord] Released audio for slot {_mySlot}");
        }
    }

    /// <summary>
    /// On startup, mute all already-running instances and claim audio for this one.
    /// Call after Register() if starting with audio.
    /// </summary>
    public static void MuteAllOthersOnStartup(int myScrcpyPid)
    {
        lock (Lock)
        {
            EnsureOpen();
            if (_accessor == null) return;

            for (var i = 0; i < MaxSlots; i++)
            {
                if (i == _mySlot) continue;

                var pid    = _accessor.ReadInt32(i * SlotSize);
                var active = _accessor.ReadByte(i * SlotSize + 4) == 1;

                if (pid == 0 || !active) continue;

                Debugger.Show($"[AudioCoord] Startup mute: slot={i} pid={pid}");
                ScrcpyVolumeController.TrySetVolume(pid, 0f);
                WriteSlot(i, pid, false);
            }
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static void EnsureOpen()
    {
        if (_mmf != null) return;
        try
        {
            // Try to open existing map first, create if not found.
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(MapName);
            }
            catch (FileNotFoundException)
            {
                _mmf = MemoryMappedFile.CreateNew(MapName, TotalSize);
            }

            _accessor = _mmf.CreateViewAccessor(0, TotalSize);
        }
        catch (Exception ex)
        {
            Debugger.Show("[AudioCoord] Failed to open shared memory: " + ex.Message);
        }
    }

    private static void WriteSlot(int slot, int pid, bool audioActive)
    {
        if (_accessor == null) return;
        _accessor.Write(slot * SlotSize,     pid);
        _accessor.Write(slot * SlotSize + 4, (byte)(audioActive ? 1 : 0));
    }
}
