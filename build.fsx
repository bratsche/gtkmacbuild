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

type PackageInfo = {
  Url: string;
  Name: string;
  Version: string;
  ConfigFlags: string option;
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
let universalLdFlags () = ["-arch i386"; "-arch x86_64"]

let majorVersion version =
  let test = Regex.Match(version, "^[0-9]+\.[0-9]+")
  match test.Success with
  | true -> test.Groups.[0].Value
  | false -> ""

let gnuUrl (name, version) = sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" name name version
let gnomeUrl (name, version) = sprintf "http://ftp.gnome.org/gnome/sources/%s/%s/%s-%s.tar.bz2" name (majorVersion(version)) name version
let fdoUrl (name, version) = sprintf "http://%s.freedesktop.org/releases/%s-%s.tar.gz" name name version
let sfUrl (name, version) = sprintf "http://downloads.sourceforge.net/sourceforge/%s/%s-%s.tar.bz2" name name version
let cairoUrlXz (name, version) = sprintf "http://cairographics.org/releases/%s-%s.tar.xz" name version
let cairoUrl (name, version) = sprintf "http://cairographics.org/releases/%s-%s.tar.gz" name version

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

let download package =
  let url = package.Url
  let file = Path.Combine(srcDir(), Path.GetFileName url)

  trace(sprintf "Downloading file %s" url)

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

  let configFlags = match package.ConfigFlags with
                    | None -> sprintf ("--prefix=%s") (installDir())
                    | Some flags -> sprintf ("%s --prefix=%s") (flags) (installDir())

  let packageName = sprintf "%s-%s" package.Name package.Version
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "configure" configFlags
    |> ignore
  )

  package

let make package =
  trace("make")

  let packageName = sprintf "%s-%s" package.Name package.Version
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "make" "" |> ignore
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
  EnvironmentHelper.setEnvironVar "PATH" (sprintf ("%s/bin:/usr/bin:/bin") (installDir()))
  EnvironmentHelper.setEnvironVar "CFLAGS" (sprintf ("-I%s/include") (installDir()))
  EnvironmentHelper.setEnvironVar "LD_LIBRARY_PATH" (sprintf ("%s/lib") (installDir()))
  EnvironmentHelper.setEnvironVar "LDFLAGS" (sprintf ("-L%s/lib") (installDir()))
  EnvironmentHelper.setEnvironVar "C_INCLUDE_PATH" (sprintf ("%s/include") (installDir()))
  EnvironmentHelper.setEnvironVar "ACLOCAL_FLAGS" (sprintf ("-I%s/share/aclocal") (installDir()))

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
  let version = "2.69"
  { Url = gnuUrl("autoconf", version); Name = "autoconf"; Version = version; ConfigFlags = None }
  |> startBuild

Target "automake" <| fun _ ->
  let version = "1.13"
  { Url = gnuUrl("automake", version); Name = "automake"; Version = version; ConfigFlags = None }
  |> startBuild

Target "libtool" <| fun _ ->
  let version = "2.4.2"
  { Url = gnuUrl("libtool", version); Name = "libtool"; Version = version; ConfigFlags = None }
  |> startBuild

Target "pkgconfig" <| fun _ ->
  let version = "0.27"
  { Url = fdoUrl("pkg-config", version); Name = "pkg-config"; Version = version; ConfigFlags = Some("--with-internal-glib") }
  |> startBuild

Target "gettext" <| fun _ ->
  let version = "0.18.2"
  { Url = gnuUrl("gettext", version); Name = "gettext"; Version = version; ConfigFlags = None }
  |> startBuild

Target "freetype" <| fun _ ->
  let version = "2.5.0.1"
  ensureDirectory (Path.Combine(installDir(), "include", "freetype2", "freetype", "cache"))
  ensureDirectory (Path.Combine(installDir(), "include", "freetype2", "freetype", "internal"))
  { Url = sfUrl("freetype", version); Name = "freetype"; Version = version; ConfigFlags = None }
  |> startBuild

Target "libffi" <| fun _ ->
  trace("libffi")

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
  let version = "0.30.0"
  { Url = cairoUrl("pixman", version); Name = "pixman"; Version = version; ConfigFlags = None }
  |> startBuild

Target "cairo" <| fun _ ->
  let version = "1.12.14"
  { Url = cairoUrlXz("cairo", version); Name = "cairo"; Version = version; ConfigFlags = Some("--enable-pdf --enable-quartz --enable-quartz-font --enable-quartz-image --disable-xlib --without-x") }
  |> startBuild

Target "pango" <| fun _ ->
  trace("pango")

Target "gtk" <| fun _ ->
  trace("gtk")

Target "xamarin-gtk-theme" <| fun _ ->
  trace("xamarin-gtk-theme")

Target "libpng" <| fun _ ->
  let version = "1.4.12"
  { Url = sfUrl("libpng", version); Name = "libpng"; Version = version; ConfigFlags = None }
  |> startBuild

Target "libjpeg" <| fun _ ->
  { Url = "http://www.ijg.org/files/jpegsrc.v8.tar.gz"; Name = "jpeg"; Version = "8"; ConfigFlags = None}
  |> startBuild

Target "libtiff" <| fun _ ->
  let version = "4.0.3"
  let url = sprintf "http://download.osgeo.org/libtiff/tiff-%s.tar.gz" version
  { Url = url; Name = "tiff"; Version = version; ConfigFlags = None}
  |> startBuild

Target "libgif" <| fun _ ->
  trace("libgif")

Target "BuildAll" <| fun _ ->
  trace("BuildAll")

// Dependencies
// --------------------------------------------------------
"prep" <== ["autoconf"; "automake"; "libtool"; "gettext"; "pkgconfig"]
"atk" <== ["glib"]
"cairo" <== ["fontconfig"; "glib"; "pixman"]
"fontconfig" <== ["freetype"]
"gdk-pixbuf" <== ["glib"; "libpng"]
"glib" <== ["libffi"]
"gtk" <== ["atk"; "gdk-pixbuf"; "pango"]
"harfbuzz" <== ["freetype"; "glib"]
"pango" <== ["cairo"; "harfbuzz"]
"pixman" <== ["libpng"; "libjpeg"; "libtiff"; "libgif"]
"xamarin-gtk-theme" <== ["gtk"]
"BuildAll" <== ["prep"; "gtk"; "xamarin-gtk-theme"]

RunTargetOrDefault "BuildAll"
