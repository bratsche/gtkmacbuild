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

type OS =
  Mac | Windows

  static member Current =
    match Environment.OSVersion.Platform with
    | PlatformID.Unix -> Mac
    | _ -> Windows

// Some directories
// ------------------------------------------------------
let systemMonoDir = "/Library/Frameworks/Mono.framework/Versions/Current"
let installDir () = Path.Combine(originDir, "install", "gtk", "Win32")
let buildDir () = Path.Combine(originDir, "root")
let binDir () = Path.Combine(installDir(), "bin")
let libDir () = Path.Combine(installDir(), "lib")
let srcDir () = Path.Combine(originDir, "src")

// Some utility functions
// ------------------------------------------------------
let gnuUrl (name, version) = sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" name name version
let universalLdFlags () = ["-arch i386"; "-arch x86_64"]

let sh command args =
  let exitCode = ProcessHelper.ExecProcess (fun info ->
    info.FileName <- command
    info.Arguments <- args) TimeSpan.MaxValue

  if exitCode <> 0 then
    let errorMsg = sprintf "Executing %s failed with exit code %d." command exitCode
    raise (BuildException(errorMsg, []))

let from (action: unit -> unit) (path: string) =
  pushd path
  action ()
  popd()

let arch = sh "uname" "-m"

let configure = sh (sprintf ("./configure --prefix=%s") (buildDir())) |> ignore
let make = sh ("make") |> ignore
let install = sh ("make install") |> ignore

let withEnvironment (name: string) (value: string) (action: unit -> unit) =
  let oldEnv = environVarOrNone name
  setProcessEnvironVar name value

  action ()

  match oldEnv with
  | None    -> ()
  | Some(x) -> setProcessEnvironVar name x

let build =
  ensureDirectory (buildDir())
  configure
  make
  install

// Targets
// --------------------------------------------------------
Target "prep" <| fun _ ->
  trace("prep")

Target "autoconf" <| fun _ ->
  trace("autoconf")

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
