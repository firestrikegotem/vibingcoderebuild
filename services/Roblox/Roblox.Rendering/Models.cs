namespace Roblox.Rendering
{
    public class RenderRequest
    {
        public string command { get; set; } = null!;
        public IEnumerable<dynamic> args { get; set; } = null!;
        public string id { get; set; } = null!;
    }

    public class RenderResponse<T>
    {
        public string id { get; set; } = null!;
        public T? data { get; set; }
        public int status { get; set; }
    }

    public class AvatarBodyColors
    {
        public int headColorId { get; set; }
        public int torsoColorId { get; set; }
        public int rightArmColorId { get; set; }
        public int leftArmColorId { get; set; }
        public int rightLegColorId { get; set; }
        public int leftLegColorId { get; set; }
    }

    public class AvatarAssetTypeEntry
    {
        public int id { get; set; }
    }
    
    public class AvatarAssetEntry
    {
        public long id { get; set; }
        public AvatarAssetTypeEntry assetType { get; set; } = null!;
        /// <summary>
        /// For clothing (Shirt 11, Pants 12, T-Shirt 2): the image asset holding the picture itself.
        /// The renderer applies clothing straight from this, so it never has to fetch and unpack the
        /// clothing item, which is the step that used to fail and leave the avatar without its clothes.
        /// Zero for everything else.
        /// </summary>
        public long textureId { get; set; }
    }
    
    public class AvatarData
    {
        public long userId { get; set; }
        public AvatarBodyColors bodyColors { get; set; } = null!;
        public string playerAvatarType { get; set; } = null!;
        public IEnumerable<AvatarAssetEntry> assets { get; set; } = null!;
    }
}

