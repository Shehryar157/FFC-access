namespace FFCAccess
{
    /// <summary>
    /// The game stacks "input layers": each screen adds one, and the top layer that blocks input gets the keys
    /// (along with any non-blocking layers above it). This answers "is that screen the one taking input right now?"
    /// </summary>
    internal static class InputLayers
    {
        /// <summary>True if a layer whose id starts with idPrefix receives input (no blocking layer above it).</summary>
        public static bool Receiving(string idPrefix)
        {
            InputLayerManager ilm = InputLayerManager.instance;
            if (ilm == null || ilm.inputLayers == null)
            {
                return false;
            }
            for (int i = ilm.inputLayers.Count - 1; i >= 0; i--)
            {
                InputLayer layer = ilm.inputLayers[i];
                if (layer == null)
                {
                    continue;
                }
                if (layer.id != null && layer.id.StartsWith(idPrefix))
                {
                    return true;
                }
                if (layer.blockInputs)
                {
                    return false;
                }
            }
            return false;
        }
    }
}
