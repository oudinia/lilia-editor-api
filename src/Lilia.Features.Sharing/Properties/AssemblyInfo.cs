// The localizer finds a type's strings under the assembly's root namespace. SharedMessages' .resx files
// moved here from Lilia.Api with the Sharing slice (2 Nov 2026); with this, and <RootNamespace> in the
// project, they keep their names (Lilia.Api.Resources.Services.SharedMessages) and nothing prints a key.
[assembly: Microsoft.Extensions.Localization.RootNamespace("Lilia.Api")]
