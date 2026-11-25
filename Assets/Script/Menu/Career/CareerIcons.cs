using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Helpers.Extensions;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// Pictures for career rows: a band's icon (an image the players choose, kept next to the profile
    /// pictures as bands/&lt;band id&gt;) and a profile's own picture. Sprites are made once and cached.
    /// Textures decoded from images come out upside down, so rows showing them flip the icon.
    /// </summary>
    public static class CareerIcons
    {
        private static string BandsDirectory => Path.Combine(PathHelper.PersistentDataPath, "bands");

        private static string BandIconPath(Guid bandId) => Path.Combine(BandsDirectory, bandId.ToString());

        // Null values remember that a band has no icon, so the file system is not asked every frame.
        private static readonly Dictionary<Guid, Sprite> BandSprites = new();
        private static readonly Dictionary<YARGImage, Sprite> AvatarSprites = new();

        public static bool HasBandIcon(Guid bandId) => File.Exists(BandIconPath(bandId));

        /// <summary>
        /// The band's icon, or null when it has none.
        /// </summary>
        public static Sprite BandIcon(Guid bandId)
        {
            if (BandSprites.TryGetValue(bandId, out var sprite))
            {
                return sprite;
            }

            sprite = null;
            var path = BandIconPath(bandId);
            if (File.Exists(path))
            {
                try
                {
                    using var image = YARGImage.Load(path);
                    if (image is { IsValid: true })
                    {
                        sprite = ToSprite(image.LoadTexture(false));
                    }
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Career: failed to load a band icon.");
                }
            }

            BandSprites[bandId] = sprite;
            return sprite;
        }

        /// <summary>
        /// Lets the player choose a picture for the band (as profile pictures are chosen).
        /// </summary>
        public static void ChooseBandIcon(CareerBand band, Action onChanged)
        {
            FileExplorerHelper.OpenChooseFile(null, new[] { "png", "jpg", "jpeg" }, path =>
            {
                try
                {
                    Directory.CreateDirectory(BandsDirectory);
                    File.Copy(path, BandIconPath(band.Id), true);
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Career: failed to save the band icon.");
                    return;
                }

                Forget(band.Id);
                onChanged?.Invoke();
            });
        }

        public static void RemoveBandIcon(CareerBand band)
        {
            var path = BandIconPath(band.Id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Forget(band.Id);
        }

        /// <summary>
        /// The profile's own picture, or null when it has none (rows then show the generic person or
        /// bot picture, as the profiles page does).
        /// </summary>
        public static Sprite ProfilePicture(YargProfile profile)
        {
            var avatar = profile?.Avatar;
            if (profile is null || profile.IsBot || avatar is not { IsValid: true })
            {
                return null;
            }

            if (!AvatarSprites.TryGetValue(avatar, out var sprite))
            {
                sprite = ToSprite(avatar.LoadTexture(false));
                AvatarSprites[avatar] = sprite;
            }

            return sprite;
        }

        private static void Forget(Guid bandId)
        {
            if (BandSprites.TryGetValue(bandId, out var old) && old != null)
            {
                UnityEngine.Object.Destroy(old.texture);
                UnityEngine.Object.Destroy(old);
            }

            BandSprites.Remove(bandId);
        }

        private static Sprite ToSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
