#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Core.Logging;

namespace YARG.Input
{
    /// <summary>
    /// Finds out which USB device a controller is, for <see cref="DeviceIdentity"/>.
    /// </summary>
    /// <remarks>
    /// The input system describes a controller by product name and version only, so two of the same
    /// model are indistinguishable and neither says which USB device (with its real name, serial number
    /// and port) it is. The system knows, on the joystick's input node. A controller is matched to its
    /// node when there is only one it can be, and otherwise by a button press: the node that reported a
    /// press at the moment the controller did.
    /// </remarks>
    public static class LinuxDeviceIdentifier
    {
        private const string USB_DEVICES = "/sys/bus/usb/devices";

        // How long before the input system reports a press its node may have reported it
        private const double PRESS_WINDOW_SECONDS = 0.25;
        private const float SCAN_INTERVAL_SECONDS = 1f;

        // struct input_event: a timeval, then type (u16), code (u16) and value (s32)
        private static readonly int _eventSize = IntPtr.Size == 8 ? 24 : 16;

        private const ushort EV_KEY = 1;
        private const ushort EV_ABS = 3;
        private const ushort ABS_HAT_FIRST = 0x10;
        private const ushort ABS_HAT_LAST = 0x17;

        private sealed class Node
        {
            public string EventPath;
            public string Version;
            public string Product;
            public string Serial;
            public string Port;

            public InputDevice Owner;

            public long LastPressTimestamp;
            public volatile bool Gone;
            public volatile bool Unreadable;
        }

        // By the node's path in sysfs
        private static readonly Dictionary<string, Node> _nodes = new();
        private static readonly Dictionary<InputDevice, bool> _wasPressed = new();
        private static readonly Dictionary<InputDevice, string> _versions = new();

        private static readonly List<InputDevice> _unidentified = new();
        private static readonly List<InputDevice> _sameVersion = new();
        private static readonly List<Node> _free = new();

        private static bool _started;
        private static bool _rescan = true;
        private static float _nextScanTime;
        private static bool _warnedUnreadable;

        public static void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _rescan = true;
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        public static void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            InputSystem.onDeviceChange -= OnDeviceChange;

            foreach (var node in _nodes.Values)
            {
                node.Gone = true;
            }

            _nodes.Clear();
            _wasPressed.Clear();
            _versions.Clear();
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                    _rescan = true;
                    break;
                case InputDeviceChange.Removed:
                    foreach (var node in _nodes.Values)
                    {
                        if (node.Owner == device)
                        {
                            node.Owner = null;
                        }
                    }

                    _wasPressed.Remove(device);
                    _versions.Remove(device);
                    DeviceIdentity.ClearUsbIdentity(device);
                    _rescan = true;
                    break;
            }
        }

        /// <summary>
        /// Call once per input update, after the devices' state has been updated.
        /// </summary>
        public static void Update()
        {
            if (!_started)
            {
                return;
            }

            _unidentified.Clear();
            foreach (var device in InputSystem.devices)
            {
                if (!DeviceIdentity.HasUsbIdentity(device) && GetVersion(device) is not null)
                {
                    _unidentified.Add(device);
                }
            }

            if (_unidentified.Count == 0)
            {
                return;
            }

            if (_rescan || Time.unscaledTime >= _nextScanTime)
            {
                _rescan = false;
                _nextScanTime = Time.unscaledTime + SCAN_INTERVAL_SECONDS;

                try
                {
                    Scan();
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Could not look for the USB devices of the connected controllers");
                }
            }

            // The controllers that only just had a button pressed
            long now = Stopwatch.GetTimestamp();
            InputDevice pressed = null;
            int pressedCount = 0;
            foreach (var device in _unidentified)
            {
                bool isPressed = device.enabled && DeviceIdentity.IsAnyButtonPressed(device);
                bool wasPressed = _wasPressed.GetValueOrDefault(device);
                _wasPressed[device] = isPressed;

                if (isPressed && !wasPressed)
                {
                    pressed = device;
                    pressedCount++;
                }
            }

            while (_unidentified.Count > 0)
            {
                string version = GetVersion(_unidentified[0]);

                _sameVersion.Clear();
                for (int i = _unidentified.Count - 1; i >= 0; i--)
                {
                    if (GetVersion(_unidentified[i]) == version)
                    {
                        _sameVersion.Add(_unidentified[i]);
                        _unidentified.RemoveAt(i);
                    }
                }

                _free.Clear();
                foreach (var node in _nodes.Values)
                {
                    if (node.Owner is null && !node.Gone && node.Version == version)
                    {
                        _free.Add(node);
                    }
                }

                if (_sameVersion.Count == 1 && _free.Count == 1)
                {
                    // It can only be this one
                    Bind(_sameVersion[0], _free[0]);
                    continue;
                }

                // One press, on one controller and on one node. Two at once say nothing; the next will.
                if (pressedCount != 1 || !_sameVersion.Contains(pressed))
                {
                    continue;
                }

                Node match = null;
                int matches = 0;
                foreach (var node in _free)
                {
                    long last = Interlocked.Read(ref node.LastPressTimestamp);
                    if (last != 0 && (now - last) / (double) Stopwatch.Frequency <= PRESS_WINDOW_SECONDS)
                    {
                        match = node;
                        matches++;
                    }
                }

                if (matches == 1)
                {
                    Bind(pressed, match);
                }
            }
        }

        private static void Bind(InputDevice device, Node node)
        {
            node.Owner = device;
            _wasPressed.Remove(device);

            YargLogger.LogInfo($"Controller {device.displayName} is USB device {node.Product} " +
                $"(serial {node.Serial ?? "none"}, port {node.Port})");

            DeviceIdentity.SetUsbIdentity(device, new DeviceIdentity.UsbIdentity(node.Product, node.Serial, node.Port));
        }

        // The input system gives a controller's USB version number (bcdDevice) in decimal;
        // sysfs has it as four hex digits
        private static string GetVersion(InputDevice device)
        {
            // Asked every frame while a controller is unidentified
            if (_versions.TryGetValue(device, out string cached))
            {
                return cached;
            }

            var description = device.description;
            string version = description.interfaceName == "SDL" && int.TryParse(description.version, out int number)
                ? number.ToString("x4")
                : null;

            _versions[device] = version;
            return version;
        }

        private static void Scan()
        {
            if (!Directory.Exists(USB_DEVICES))
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (string usbInterface in Directory.GetDirectories(USB_DEVICES, "*:*"))
            {
                string inputs = Path.Combine(usbInterface, "input");
                if (!Directory.Exists(inputs))
                {
                    continue;
                }

                foreach (string input in Directory.GetDirectories(inputs, "input*"))
                {
                    // Only joysticks, and the event node that carries their buttons
                    if (Directory.GetDirectories(input, "js*").Length == 0)
                    {
                        continue;
                    }

                    var events = Directory.GetDirectories(input, "event*");
                    if (events.Length == 0)
                    {
                        continue;
                    }

                    seen.Add(input);
                    if (_nodes.ContainsKey(input))
                    {
                        continue;
                    }

                    string port = Path.GetFileName(usbInterface);
                    port = port[..port.IndexOf(':')];
                    string usbDevice = Path.Combine(USB_DEVICES, port);

                    string serial = ReadTrimmed(Path.Combine(usbDevice, "serial"));
                    var node = new Node
                    {
                        EventPath = "/dev/input/" + Path.GetFileName(events[0]),
                        Version = ReadTrimmed(Path.Combine(input, "id", "version")),
                        Product = ReadTrimmed(Path.Combine(usbDevice, "product")) ?? ReadTrimmed(Path.Combine(input, "name")),
                        Serial = string.IsNullOrEmpty(serial) ? null : serial,
                        Port = port,
                    };

                    _nodes[input] = node;

                    new Thread(() => ReadPresses(node))
                    {
                        IsBackground = true,
                        Name = "Controller press reader " + node.EventPath,
                    }.Start();
                }
            }

            var gone = new List<string>();
            foreach (var (path, node) in _nodes)
            {
                if (!seen.Contains(path))
                {
                    node.Gone = true;
                    gone.Add(path);
                }
                else if (node.Unreadable && !_warnedUnreadable)
                {
                    _warnedUnreadable = true;
                    YargLogger.LogFormatWarning(
                        "Cannot read {0}; identical controllers cannot be told apart by a button press",
                        node.EventPath);
                }
            }

            foreach (string path in gone)
            {
                _nodes.Remove(path);
            }
        }

        // Runs on its own thread until the node goes away
        private static void ReadPresses(Node node)
        {
            try
            {
                using var stream = new FileStream(node.EventPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite, 1, false);

                var buffer = new byte[_eventSize * 32];
                int valueOffset = _eventSize - 4;
                while (!node.Gone)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    for (int i = 0; i + _eventSize <= read; i += _eventSize)
                    {
                        ushort type = BitConverter.ToUInt16(buffer, i + valueOffset - 4);
                        ushort code = BitConverter.ToUInt16(buffer, i + valueOffset - 2);
                        int value = BitConverter.ToInt32(buffer, i + valueOffset);

                        // A button going down, or a d-pad (strum bar) direction
                        bool press = (type == EV_KEY && value == 1) ||
                            (type == EV_ABS && code >= ABS_HAT_FIRST && code <= ABS_HAT_LAST && value != 0);
                        if (press)
                        {
                            Interlocked.Exchange(ref node.LastPressTimestamp, Stopwatch.GetTimestamp());
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Unplugged, or not ours to read
                node.Unreadable = !node.Gone;
            }
        }

        private static string ReadTrimmed(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
    }
}
#endif
