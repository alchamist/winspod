using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Winspod II")]
[assembly: AssemblyDescription("The second generation of WinSpod")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Julian Eames")]
[assembly: AssemblyProduct("Winspod II")]
[assembly: AssemblyCopyright("Copyright ©  2009")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("61aebb55-abc0-46ab-9348-6b0f214fce35")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
//
// Left at the project template's default "1.0.0.0" since 2009 and never corrected,
// despite AssemblyProduct/AssemblyDescription above both already saying "Winspod II" /
// "the second generation" - this codebase is that second generation (a full C# rewrite
// of the original VB one), so 1.x was always wrong for what's actually running. Set to
// 2.5.0 here: major 2 for the rewrite itself, minor reflecting the real milestones
// since (Docker, TLS-wrapped telnet, the WebSocket bridge, NAWS-driven line wrapping,
// and the give/socials/economy work) rather than understating it as a fresh 2.0. No
// automation keeps this current going forward (there wasn't any before either), so
// treat it as a manually-maintained "which era of the codebase is this" marker, not a
// per-change counter - the git commit shown alongside it (Server.GitCommit, `version`
// command, /api/status) is what answers "is this the exact build I just pushed", which
// this number was never suited for anyway.
[assembly: AssemblyVersion("2.5.0.0")]
[assembly: AssemblyFileVersion("2.5.0.0")]

