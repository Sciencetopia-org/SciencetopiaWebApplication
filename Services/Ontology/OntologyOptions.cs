namespace Sciencetopia.Services.Ontology
{
    /// <summary>
    /// Ontology V2 feature flags. Mirrors the existing options pattern
    /// (Sciencetopia.Services.L10n.L10nOptions) and is bound from the "Ontology"
    /// config section in Program.cs.
    ///
    /// Phase 1: definitions only. ALL flags default to false and NOTHING reads them
    /// yet — registering the options binds config but changes no runtime behavior.
    /// Do not enable any flag until the corresponding phase is validated (plan §13).
    /// </summary>
    public class OntologyOptions
    {
        public bool OntologyV2Enabled { get; set; } = false;
        public bool TaggingV2Enabled { get; set; } = false;
        public bool L10nV2Enabled { get; set; } = false;
        public bool GraphProjectionV2Enabled { get; set; } = false;
        public bool GraphRagSearchEnabled { get; set; } = false;
    }
}
