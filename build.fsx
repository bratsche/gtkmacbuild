#I "tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Net
open System.Text.RegularExpressions
open Fake
open Fake.FileUtils
open Fake.FileHelper
open Fake.StringHelper
open Fake.Git
open Fake.EnvironmentHelper

let originDir = FileSystemHelper.currentDirectory

type PackageType =
  GnuPackage | GnomePackage

type PackageInfo = {
  Source: PackageType;
  Name: string;
  Version: string;
}

type Arch =
  X86 | X64 | Universal

type OS =
  Mac | Windows

  static member Current =
    match Environment.OSVersion.Platform with
    | PlatformID.Unix -> Mac
    | _ -> Windows

// Some directories
// ------------------------------------------------------
let systemMonoDir = "/Library/Frameworks/Mono.framework/Versions/Current"
let installDir () = Path.Combine(originDir, "install")
let buildDir () = Path.Combine(originDir, "root")
let binDir () = Path.Combine(installDir(), "bin")
let libDir () = Path.Combine(installDir(), "lib")
let srcDir () = Path.Combine(originDir, "src")

// Some utility functions
// ------------------------------------------------------
let gnuUrl (name, version) = sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" name name version
let universalLdFlags () = ["-arch i386"; "-arch x86_64"]

let majorVersion version =
  let test = Regex.Match(version, "^[0-9]+\.[0-9]+")
  match test.Success with
  | true -> test.Groups.[0].Value
  | false -> ""

let from (action: unit -> unit) (path: string) =
  pushd path
  action ()
  popd()

let sh command args =
  let exitCode = ProcessHelper.ExecProcess (fun info ->
    info.FileName <- command
    info.Arguments <- args) TimeSpan.MaxValue

  if exitCode <> 0 then
    let errorMsg = sprintf "Executing %s failed with exit code %d." command exitCode
    raise (BuildException(errorMsg, []))

let filenameFromUrl (url: string) =
  url.Split('/')
  |> Array.toList
  |> List.rev
  |> List.head

let packageUrl package =
  match package.Source with
  | GnuPackage -> sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" package.Name package.Name package.Version
  | GnomePackage -> sprintf "http://ftp.gnome.org/gnome/sources/%s/%s/%s-%s.tar.bz2" package.Name (majorVersion(package.Version)) package.Name package.Version

let download package =
  let url = packageUrl package
  let file = Path.Combine(srcDir(), Path.GetFileName url)

  if not (File.Exists (file)) then
    use client = new WebClient() in
      client.DownloadFile(url, file)

  (file, package)

let extract (filename, package) =
  trace(sprintf ("extract %s") (filename))
  sprintf ("-C %s -xf %s") (buildDir()) (filename) |> sh "tar"

  (filename, package)

let arch = sh "uname" "-m"

let configure package =
  trace("configure")

  let packageName = sprintf "%s-%s" package.Name package.Version
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh ("configure") (sprintf ("--prefix=%s") (installDir()))
    |> ignore
  )

  package

let make package =
  trace("make")

  let packageName = sprintf "%s-%s" package.Name package.Version
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "make" |> ignore
  )

  package

let install package =
  let packageName = sprintf "%s-%s" package.Name package.Version
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "make" "install" |> ignore
  )

  package

let withEnvironment (name: string) (value: string) (action: unit -> unit) =
  let oldEnv = environVarOrNone name
  setProcessEnvironVar name value

  action ()

  match oldEnv with
  | None    -> ()
  | Some(x) -> setProcessEnvironVar name x

let build (filename, package) =
  ensureDirectory (installDir())

  package
  |> configure
  |> make
  |> install

let startBuild package =
  package
  |> download
  |> extract
  |> build
  |> ignore

// Targets
// --------------------------------------------------------
Target "prep" <| fun _ ->
  trace("prep")

Target "autoconf" <| fun _ ->
  trace("autoconf")

  { Source = GnuPackage; Name = "autoconf"; Version = "2.69" }
  |> startBuild

Target "automake" <| fun _ ->
  trace("automake")
  { Source = GnuPackage; Name = "automake"; Version = "1.13" }
  |> startBuild

Target "freetype" <| fun _ ->
  trace("freetype")

Target "libffi" <| fun _ ->
  trace("libffi")

Target "gettext-runtime" <| fun _ ->
  trace("gettext-runtime")

Target "glib" <| fun _ ->
  trace("glib")

Target "harfbuzz" <| fun _ ->
  trace("harfbuzz")

Target "atk" <| fun _ ->
  trace("atk")

Target "gdk-pixbuf" <| fun _ ->
  trace("gdk-pixbuf")

Target "fontconfig" <| fun _ ->
  trace("fontconfig")

Target "pixman" <| fun _ ->
  trace("pixman")

Target "cairo" <| fun _ ->
  trace("cairo")

Target "pango" <| fun _ ->
  trace("pango")

Target "gtk" <| fun _ ->
  trace("gtk")

Target "xamarin-gtk-theme" <| fun _ ->
  trace("xamarin-gtk-theme")

Target "zlib" <| fun _ ->
  trace("zlib")

Target "libpng" <| fun _ ->
  trace("libpng")

Target "BuildAll" <| fun _ ->
  trace("BuildAll")

// Dependencies
// --------------------------------------------------------
"prep" <== ["autoconf"; "automake"]
"atk" <== ["glib"]
"cairo" <== ["fontconfig"; "glib"; "pixman"]
"fontconfig" <== ["freetype"]
"gdk-pixbuf" <== ["glib"; "libpng"]
"glib" <== ["libffi"; "gettext-runtime"; "zlib"]
"gtk" <== ["atk"; "gdk-pixbuf"; "pango"]
"harfbuzz" <== ["freetype"; "glib"]
"libpng" <== ["zlib"]
"pango" <== ["cairo"; "harfbuzz"]
"pixman" <== ["libpng"]
"xamarin-gtk-theme" <== ["gtk"]
"BuildAll" <== ["prep"; "gtk"; "xamarin-gtk-theme"]

RunTargetOrDefault "BuildAll"
