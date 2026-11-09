namespace FolioXR.Tools
{
    /// <summary>Entrada real del registro; D/E reemplazarán este componente por su herramienta.</summary>
    public sealed class UnavailableTool : FolioTool
    {
        public override bool Available => false;
    }
}
