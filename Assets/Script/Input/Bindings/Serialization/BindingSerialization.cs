using DG.Tweening.Plugins.Core.PathCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using YARG.Audio;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Input.Bindings;
using YARG.Menu.ProfileList;
using static YARG.Input.Serialization.SerializedBindingsV4;

#nullable enable

namespace YARG.Input.Serialization
{
    // These classes are what the bindings will use for serialization/deserialization.
    // They are *not* what will get written to the bindings file in the end, however,
    // since bindings are versioned. These are used to separate the written format and actual loaded format,
    // and to make adding things easier, since each layer of the serialization has its own type.
    //
    // When making changes to the bindings format, create a copy of the current `BindingsVersion.vX.cs` file
    // and make your changes to that. **Do not modify the existing version files!**
    // Next, make changes to the classes below, if needed, e.g. new data needs to be stored/loaded.
    // Finally, update SerializeBindings/DeserializeBindings here to reflect the new version:
    // - Make SerializeBindings serialize to the new version of the format.
    // - Add a new case branch to DeserializeBindings for the new version.

    public class SerializedBindings
    {
        public Dictionary<Guid, SerializedPlayerDeviceInfo> Profiles = new();
        public Dictionary<Guid, SerializedReusableBindingSet> ReusableBindingSets = new();
    }

    public class SerializedPlayerDeviceInfo
    {
        public List<SerializedInputDevice> Controllers = new();
        public List<SerializedMic> Microphones = new();

        // Legacy single microphone, only filled when loading v0-v2 files
        public SerializedMic? Microphone;

        public Dictionary<GameMode, SerializedModeMappingCollection> ModeMappings = new();
    }

    public class SerializedModeMappingCollection
    {
        public Dictionary<ControllerFamily, Guid> MappingsByControllerFamily = new();
    }

    public class SerializedReusableBindingSet
    {
        public SerializedReusableBindingSet(string name, ControllerFamily family, GameMode mode, string description = "")
        {
            Name = name;
            Family = family;
            Mode = mode;
            Description = description;
        }

        public string Name;
        public string Description;
        public Guid? Guid;
        public int? Version;
        public GameMode Mode;
        public ControllerFamily Family;

        // Key is binding name, like "FiveFret.Green" or "FourDrums.RedPad"
        // These names come from BindingCollection.Templates.cs; they are YARG's, not PlasticBand's
        public Dictionary<string, SerializedReusableControlBinding> Bindings = new();
    }

    public class SerializedReusableControlBinding
    {
        public Dictionary<string, string> Parameters = new();
        public List<SerializedSingleBinding> Controls = new();
    }

    public class SerializedInputDevice
    {
        public string BaseLayout;
        public string Layout;
        public string Hash;

        public SerializedInputDevice(string baseLayout, string layout, string hash)
        {
            BaseLayout = baseLayout;
            Layout = layout;
            Hash = hash;
        }

        public SerializedInputDevice(InputDevice device)
        {
            BaseLayout = LayoutHelper.LayoutStringToSourceLayout(device.layout);
            Layout = device.layout;
            Hash = device.GetHash();
        }

        public bool MatchesDevice(InputDevice device)
        {
            // Entries saved before a device's hash became independent of the connection order (MIDI
            // devices) still carry the old one; they get the new one the next time they are saved
            return Layout == device.layout && (Hash == device.GetHash() || Hash == device.GetLegacyHash());
        }
    }

    public class SerializedSingleBinding
    {
        public string SourceLayout;

        public string ControlName;

        public Dictionary<string, string> Parameters = new();

        public SerializedSingleBinding(string controlName, string sourceLayout)
        {
            ControlName = controlName;
            SourceLayout = sourceLayout;
        }
    }

    public static partial class BindingSerialization
    {
        public const int CURRENT_VERSION = 4;

        public const string REPORT_ID_SUFFIX = "_ReportId";

        private static readonly SHA1 _hashAlgorithm = SHA1.Create();
        private static readonly Regex _xinputUserIndexRegex = new(@"\\""userIndex\\"":\s*\d,");

        private static readonly Dictionary<InputDevice, string> _hashCache = new();
        private static readonly Dictionary<InputDevice, string> _legacyHashCache = new();

        public static SerializedInputDevice Serialize(this InputDevice device)
        {
            return new(device);
        }

        /// <summary>
        /// Identifies a device across sessions. For a MIDI device it does not depend on the order the
        /// devices were connected in, and tells apart two of the same kind when they have serial numbers.
        /// </summary>
        public static string GetHash(this InputDevice device)
        {
            return GetHash(device, _hashCache, DeviceIdentity.GetStableDescription(device));
        }

        /// <summary>
        /// Call when a device's <see cref="DeviceIdentity.GetStableDescription"/> has changed.
        /// </summary>
        public static void InvalidateHash(InputDevice device)
        {
            _hashCache.Remove(device);
        }

        /// <summary>
        /// The hash as it was before <see cref="DeviceIdentity.GetStableDescription"/>: of the whole
        /// description, which for a MIDI device includes its client number.
        /// </summary>
        public static string GetLegacyHash(this InputDevice device)
        {
            return GetHash(device, _legacyHashCache, null);
        }

        private static string GetHash(InputDevice device, Dictionary<InputDevice, string> _hashCache,
            string stableDescription)
        {
            // Check if we have a calculated hash cached already
            if (_hashCache.TryGetValue(device, out string hash))
                return hash;

            var description = device.description;
            string descriptionJson = stableDescription ?? description.ToJson();
            // Exclude user index on XInput devices
            if (description.interfaceName == "XInput")
                descriptionJson = _xinputUserIndexRegex.Replace(descriptionJson, "");

            // Calculate the hash
            var descriptionBytes = Encoding.Default.GetBytes(descriptionJson);
            var hashBytes = _hashAlgorithm.ComputeHash(descriptionBytes);
            hash = BitConverter.ToString(hashBytes).Replace("-", "");

            // Cache the calculated hash
            _hashCache.Add(device, hash);

            return hash;
        }

        public static void SerializeBindings(SerializedBindings bindings, string bindingsPath)
        {
            try
            {
                var serialized = SerializeBindingsV4(bindings);
                string bindingsJson = JsonConvert.SerializeObject(serialized, Formatting.Indented);
                File.WriteAllText(bindingsPath, bindingsJson);
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while saving bindings!");
            }
        }

        public static SerializedBindings? DeserializeBindings(string bindingsPath)
        {
            try
            {
                if (!File.Exists(bindingsPath))
                {
                    return null;
                }

                string bindingsJson = File.ReadAllText(bindingsPath);
                var jObject = JObject.Parse(bindingsJson);

                int version = jObject["Version"] switch
                {
                    null => 0,
                    { Type: JTokenType.Integer } versionToken => (int) versionToken,
                    {} unhandled => throw new InvalidDataException($"Invalid bindings version! Expected JSON type {JTokenType.Integer}, got {unhandled.Type}")
                };

                var bindings = version switch
                {
                    0 => DeserializeBindingsV0(jObject), 
                    1 => DeserializeBindingsV1(jObject),
                    2 => DeserializeBindingsV2(jObject),
                    3 => DeserializeBindingsV3(jObject),
                    4 => DeserializeBindingsV4(jObject),
                    _ => throw new NotImplementedException($"Unhandled bindings version {version}!")
                };

                return bindings;
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while loading bindings!");
                return null;
            }
        }

        public static SerializedReusableBindingSet? DeserializeImport(string importPath)
        {
            try
            {
                if (!File.Exists(importPath))
                {
                    return null;
                }

                var text = File.ReadAllText(importPath);
                var jObject = JObject.Parse(text);

                int version = jObject["Version"] switch
                {
                    null => 0,
                    { Type: JTokenType.Integer } versionToken => (int) versionToken,
                    { } unhandled => throw new InvalidDataException($"Invalid bindings version! Expected JSON type {JTokenType.Integer}, got {unhandled.Type}")
                };

                var deserialized = version switch {
                    4 => DeserializeBindingSetV4(jObject),
                    _ => throw new NotImplementedException($"Unhandled bindings version {version}!")
                };

                return deserialized;
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Failed to import binding set.");
                return null;
            }
        }
    }
}