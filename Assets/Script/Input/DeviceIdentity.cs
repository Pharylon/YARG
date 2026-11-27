using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using YARG.Core;
using YARG.Core.Logging;

namespace YARG.Input
{
    /// <summary>
    /// What to call an input device and how to recognize it again, where the input system's own
    /// description is too generic (every XInput-style guitar is an "Xbox 360 Controller") or changes with
    /// the order devices were plugged in (a MIDI port's client number).
    /// </summary>
    public static class DeviceIdentity
    {
        private const string MIDI_INTERFACE = "Minis";

        // The ALSA "client:port" pair in a MIDI port name, e.g. the " 24:0" of
        // "Alesis Drum Module:Alesis Drum Module MIDI 1 24:0 (All Channels)".
        // The client number depends on the order the devices were connected in.
        private static readonly Regex _midiClientPortRegex = new(@"\s(\d+):(\d+)(?=\s*(\(|$))");

        /// <summary>
        /// The USB device behind a controller, where the platform can tell (see LinuxDeviceIdentifier).
        /// </summary>
        public readonly struct UsbIdentity
        {
            public readonly string Product;
            public readonly string Serial;
            public readonly string Port;

            public UsbIdentity(string product, string serial, string port)
            {
                Product = product;
                Serial = serial;
                Port = port;
            }
        }

        /// <summary>
        /// A device's <see cref="GetStableDescription"/> and name changed: it was found to be a
        /// particular USB device.
        /// </summary>
        public static event Action<InputDevice> Identified;

        private static readonly Dictionary<InputDevice, UsbIdentity> _usbIdentities = new();

        public static bool HasUsbIdentity(InputDevice device) => _usbIdentities.ContainsKey(device);

        public static void SetUsbIdentity(InputDevice device, UsbIdentity identity)
        {
            _usbIdentities[device] = identity;
            _stableDescriptions.Remove(device);
            _baseNames.Clear();

            Identified?.Invoke(device);
        }

        public static void ClearUsbIdentity(InputDevice device)
        {
            _usbIdentities.Remove(device);
            _stableDescriptions.Remove(device);
            _baseNames.Clear();
        }

        private static readonly Dictionary<InputDevice, string> _baseNames = new();
        private static readonly Dictionary<InputDevice, string> _stableDescriptions = new();

        static DeviceIdentity()
        {
            // Names and numbering depend on which other devices are present
            InputSystem.onDeviceChange += (_, change) =>
            {
                if (change is InputDeviceChange.Added or InputDeviceChange.Removed)
                {
                    _baseNames.Clear();
                }
            };
        }

        /// <summary>
        /// The name to show for a device: the product's own name where it can be found, and a number
        /// when several connected devices would otherwise read the same.
        /// </summary>
        public static string GetName(InputDevice device)
        {
            string name = GetBaseName(device);

            int index = 0;
            int count = 0;
            foreach (var other in InputSystem.devices)
            {
                if (GetBaseName(other) != name)
                {
                    continue;
                }

                count++;
                if (other == device)
                {
                    index = count;
                }
            }

            return count > 1 && index > 0 ? $"{name} {index}" : name;
        }

        private static readonly Dictionary<InputDevice, GameMode> _instrumentHints = new();

        /// <summary>
        /// Records what kind of instrument the player said a device is, as a game mode of that kind,
        /// for a device that presents itself as a plain gamepad. Kept for the session.
        /// </summary>
        public static void SetInstrumentHint(InputDevice device, GameMode? mode)
        {
            if (mode is null)
            {
                _instrumentHints.Remove(device);
            }
            else
            {
                _instrumentHints[device] = mode.Value;
            }
        }

        /// <summary>
        /// Whether a device is the kind of instrument <paramref name="mode"/> is played on, going by
        /// what the player said it is or else by its name ("CRKD Guitar", "Alesis Drum Module"). Null
        /// when neither says: bindings exist to play any mode on a gamepad or a MIDI port, so this is
        /// the only thing that keeps drums from being offered on a guitar.
        /// </summary>
        public static bool? FitsGameMode(InputDevice device, GameMode mode)
        {
            if (_instrumentHints.TryGetValue(device, out var hint))
            {
                return InstrumentKind(hint) == InstrumentKind(mode);
            }

            string name = GetBaseName(device).ToLowerInvariant();
            bool isGuitar = name.Contains("guitar");
            bool isDrums = name.Contains("drum");
            bool isKeys = name.Contains("key") || name.Contains("piano");
            if ((isGuitar ? 1 : 0) + (isDrums ? 1 : 0) + (isKeys ? 1 : 0) != 1)
            {
                return null;
            }

            return InstrumentKind(mode) == (isGuitar ? 0 : isDrums ? 1 : 2);
        }

        private static int InstrumentKind(GameMode mode)
        {
            return mode switch
            {
                GameMode.FiveFretGuitar or GameMode.SixFretGuitar or GameMode.ProGuitar => 0,
                GameMode.FourLaneDrums or GameMode.FiveLaneDrums or GameMode.EliteDrums => 1,
                GameMode.ProKeys => 2,
                _ => 3,
            };
        }

        /// <summary>
        /// Whether any button of the device is held right now. For showing which physical device is which.
        /// </summary>
        public static bool IsAnyButtonPressed(InputDevice device)
        {
            if (device is null || !device.added)
            {
                return false;
            }

            foreach (var control in device.allControls)
            {
                if (control is ButtonControl { synthetic: false, isPressed: true })
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A description that picks out this one device and stays the same across sessions, for the
        /// devices whose own description does not: a controller found to be a particular USB device
        /// (its serial number, or failing that its port), and a MIDI device (the port name without its
        /// client number, which depends on the order devices were connected in, plus the USB serial
        /// number when the system gives one). Null for other devices.
        /// </summary>
        public static string GetStableDescription(InputDevice device)
        {
            if (_usbIdentities.TryGetValue(device, out var usb))
            {
                return $"USB|{usb.Product}|{usb.Serial ?? "port:" + usb.Port}";
            }

            if (device.description.interfaceName != MIDI_INTERFACE)
            {
                return null;
            }

            if (_stableDescriptions.TryGetValue(device, out string stable))
            {
                return stable;
            }

            string product = device.description.product ?? string.Empty;
            string serial = null;
            try
            {
                serial = GetMidiSerial(product);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Could not look up the serial number of {product}");
            }

            stable = $"{MIDI_INTERFACE}|{StripMidiClientPort(product)}|{serial}";
            _stableDescriptions[device] = stable;
            return stable;
        }

        public static string StripMidiClientPort(string portName)
        {
            return _midiClientPortRegex.Replace(portName, "");
        }

        private static string GetBaseName(InputDevice device)
        {
            if (_baseNames.TryGetValue(device, out string name))
            {
                return name;
            }

            name = device.displayName;
            try
            {
                if (_usbIdentities.TryGetValue(device, out var usb))
                {
                    // The end of the serial number tells two of the same model apart, the same way every session
                    name = usb.Serial is { Length: > 0 } serial
                        ? $"{usb.Product} (\u2026{serial[Math.Max(0, serial.Length - 4)..]})"
                        : usb.Product;
                }
                else if (device.description.interfaceName == MIDI_INTERFACE)
                {
                    name = GetMidiName(device.description.product ?? name);
                }
                else
                {
                    name = GetUsbProductName(device) ?? name;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Could not look up the name of {device.displayName}");
            }

            _baseNames[device] = name;
            return name;
        }

        // "Alesis Drum Module:Alesis Drum Module MIDI 1 24:0 (All Channels)" -> "Alesis Drum Module MIDI 1 (All Channels)"
        private static string GetMidiName(string portName)
        {
            string name = StripMidiClientPort(portName);

            int colon = name.IndexOf(':');
            if (colon > 0 && name.Length > colon + 1 &&
                name.AsSpan(colon + 1).StartsWith(name.AsSpan(0, colon), StringComparison.Ordinal))
            {
                name = name[(colon + 1)..];
            }

            // Nearly every port is "(All Channels)"; the name is long enough without it
            const string allChannels = " (All Channels)";
            if (name.EndsWith(allChannels, StringComparison.Ordinal))
            {
                name = name[..^allChannels.Length];
            }

            return name;
        }

#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        private const string USB_DEVICES = "/sys/bus/usb/devices";

        // The input system (through SDL) names a controller after the driver that took it, so every
        // XInput-style instrument is an "Xbox 360 Controller". The USB device knows better ("CRKD Guitar").
        // The description has nothing that points at one USB device, only the device's version number, so
        // the name is used when every joystick with that version number agrees on it.
        private static string GetUsbProductName(InputDevice device)
        {
            var description = device.description;
            if (description.interfaceName != "SDL" || !int.TryParse(description.version, out int version) ||
                !Directory.Exists(USB_DEVICES))
            {
                return null;
            }

            string wanted = version.ToString("x4");
            string found = null;
            foreach (string usbInterface in Directory.GetDirectories(USB_DEVICES, "*:*"))
            {
                string inputs = Path.Combine(usbInterface, "input");
                if (!Directory.Exists(inputs))
                {
                    continue;
                }

                foreach (string input in Directory.GetDirectories(inputs, "input*"))
                {
                    if (Directory.GetDirectories(input, "js*").Length == 0 ||
                        ReadTrimmed(Path.Combine(input, "id", "version")) != wanted)
                    {
                        continue;
                    }

                    string usbDevice = Path.GetFileName(usbInterface);
                    usbDevice = usbDevice[..usbDevice.IndexOf(':')];
                    string product = ReadTrimmed(Path.Combine(USB_DEVICES, usbDevice, "product"));
                    if (string.IsNullOrEmpty(product) || (found is not null && found != product))
                    {
                        return null;
                    }

                    found = product;
                }
            }

            return found;
        }

        // ALSA gives a sound card's MIDI ports the client number 16 + 4 * card; the card says which USB
        // device it is, and that device may have a serial number.
        private static string GetMidiSerial(string portName)
        {
            var match = _midiClientPortRegex.Match(portName);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int client) ||
                client < 16 || client >= 128 || (client - 16) % 4 != 0)
            {
                return null;
            }

            // "001/026": bus and device number
            string usbBus = ReadTrimmed($"/proc/asound/card{(client - 16) / 4}/usbbus");
            var parts = usbBus?.Split('/');
            if (parts is not { Length: 2 } || !int.TryParse(parts[0], out int bus) ||
                !int.TryParse(parts[1], out int number) || !Directory.Exists(USB_DEVICES))
            {
                return null;
            }

            foreach (string usbDevice in Directory.GetDirectories(USB_DEVICES))
            {
                if (Path.GetFileName(usbDevice).Contains(':'))
                {
                    continue;
                }

                if (int.TryParse(ReadTrimmed(Path.Combine(usbDevice, "busnum")), out int deviceBus) &&
                    int.TryParse(ReadTrimmed(Path.Combine(usbDevice, "devnum")), out int deviceNumber) &&
                    deviceBus == bus && deviceNumber == number)
                {
                    string serial = ReadTrimmed(Path.Combine(usbDevice, "serial"));
                    return string.IsNullOrEmpty(serial) ? null : serial;
                }
            }

            return null;
        }

        private static string ReadTrimmed(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
#else
        private static string GetUsbProductName(InputDevice device) => null;

        private static string GetMidiSerial(string portName) => null;
#endif
    }
}
