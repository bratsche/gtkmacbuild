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

// Package type definitions
let majorVersion version =
  let test = Regex.Match(version, "^[0-9]+\.[0-9]+")
  match test.Success with
  | true -> test.Groups.[0].Value
  | false -> ""


type Version = Version of string
type Url = Url of string
type Branch = Branch of string
type Revision = Revision of string

type SourceType =
  | Gnu
  | Gnome
  | GnomeXz
  | FreeDesktop
  | SourceForge
  | Cairo
  | CairoXz

type PkgSource =
  | Git of Url * Branch * Revision
  | Tarball of Url * Version * string

type SetupMethod =
  | Configure
  | Autogen

type Pkg = {
  Name: string;
  Source: PkgSource;
  Setup: SetupMethod;
  ConfigFlags: string option;
}

let defSource (srcType: SourceType) name version =
  let url = match srcType with
            | Gnu -> sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" name name version
            | Gnome -> sprintf "http://ftp.gnome.org/gnome/sources/%s/%s/%s-%s.tar.bz2" name (majorVersion(version)) name version
            | GnomeXz -> sprintf "http://ftp.gnome.org/pub/gnome/sources/%s/%s/%s-%s.tar.xz" name (majorVersion(version)) name version
            | FreeDesktop -> sprintf "http://%s.freedesktop.org/releases/%s-%s.tar.gz" name name version
            | SourceForge -> sprintf "http://downloads.sourceforge.net/sourceforge/%s/%s-%s.tar.bz2" name name version
            | Cairo -> sprintf "http://cairographics.org/releases/%s-%s.tar.gz" name version
            | CairoXz -> sprintf "http://cairographics.org/releases/%s-%s.tar.xz" name version
  Tarball (Url(url), Version(version), name)

let gitSource url branch revno =
  Git (Url (url), Branch (branch), Revision (revno))

let defCustomSource url name version =
  Tarball (Url(url), Version(version), name)

let forPackage name source =
  { Name = name; Source = source; ConfigFlags = None; Setup = Configure }

let configurePackage setup pkg =
  {pkg with Setup = setup}

let withConfigFlags flags pkg =
  {pkg with ConfigFlags = Some(flags)}


// Some directories
// ------------------------------------------------------
let originDir = FileSystemHelper.currentDirectory
let systemMonoDir = "/Library/Frameworks/Mono.framework/Versions/Current"
let installDir () = Path.Combine(originDir, "install")
let buildDir () = Path.Combine(originDir, "root")
let binDir () = Path.Combine(installDir(), "bin")
let libDir () = Path.Combine(installDir(), "lib")
let srcDir () = Path.Combine(originDir, "src")

// Some utility functions
// ------------------------------------------------------
let universalLdFlags () = ["-arch i386"; "-arch x86_64"]


let gnuUrl (name, version) = sprintf "ftp://ftp.gnu.org/gnu/%s/%s-%s.tar.gz" name name version
let gnomeUrl (name, version) = sprintf "http://ftp.gnome.org/gnome/sources/%s/%s/%s-%s.tar.bz2" name (majorVersion(version)) name version
let fdoUrl (name, version) = sprintf "http://%s.freedesktop.org/releases/%s-%s.tar.gz" name name version
let sfUrl (name, version) = sprintf "http://downloads.sourceforge.net/sourceforge/%s/%s-%s.tar.bz2" name name version
let cairoUrlXz (name, version) = sprintf "http://cairographics.org/releases/%s-%s.tar.xz" name version
let cairoUrl (name, version) = sprintf "http://cairographics.org/releases/%s-%s.tar.gz" name version
let gnomeUrlXz (name, version) = sprintf "http://ftp.gnome.org/pub/gnome/sources/%s/%s/%s-%s.tar.xz" name (majorVersion(version)) name version

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

let extract (filename, pkg) =
  match pkg.Source with
  | Tarball (_,_,_) -> sprintf ("-C %s -xf %s") (buildDir()) (filename) |> sh "tar"
  | _ -> ()

  (filename, pkg)

let arch = sh "uname" "-m"

let extractDirectory pkg =
  match pkg.Source with
  | Git (Url(url), branch, rev) ->
    let regex = Regex.Match(url, "([A-Za-z0-9]+)\.git$")
    match regex.Success with
    | true ->
      let s = regex.Groups.[0].Value
      s.[0..(String.length(s) - 5)] // Remove the ".git" from the end
    | false -> failwith "Invalid git URL"
  | Tarball (url, Version(version), name) -> sprintf "%s-%s" name version

let download (pkg) =
  match pkg.Source with
  | Git ( Url(url), Branch(branch), Revision(rev) ) ->
    let checkoutDir = Path.Combine(buildDir(), extractDirectory(pkg))
    if not (Directory.Exists(checkoutDir)) then
      Git.Repository.cloneSingleBranch (buildDir()) url branch checkoutDir
      Git.Reset.hard checkoutDir rev |> ignore

    (checkoutDir, pkg)
  | Tarball ( Url(url), version, name) ->
    let file = Path.Combine(srcDir(), Path.GetFileName url)

    trace(sprintf "Downloading file %s" url)

    if not (File.Exists (file)) then
      use client = new WebClient() in
        client.DownloadFile(url, file)

    (file, pkg)

let configure pkg =
  let configFlags = match pkg.ConfigFlags with
                    | None -> sprintf ("--prefix=%s") (installDir())
                    | Some flags -> sprintf ("%s --prefix=%s") (flags) (installDir())
  let command = match pkg.Setup with
                | Configure -> "configure"
                | Autogen -> "autogen.sh"

  let packageName = extractDirectory pkg
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh command configFlags
    |> ignore
  )

  pkg

let make pkg =
  let packageName = extractDirectory pkg
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "make" "-j 4" |> ignore
  )

  pkg

let install pkg =
  let packageName = extractDirectory pkg
  Path.Combine(buildDir(), packageName)
  |> from (fun () ->
    sh "make" "install" |> ignore
  )

  pkg

let withEnvironment (name: string) (value: string) (action: unit -> unit) =
  let oldEnv = environVarOrNone name
  setProcessEnvironVar name value

  action ()

  match oldEnv with
  | None    -> ()
  | Some(x) -> setProcessEnvironVar name x

let build (filename, pkg) =
  ensureDirectory (installDir())

  pkg
  |> configure
  |> make
  |> install

let startBuild pkg =
  EnvironmentHelper.setEnvironVar "PATH" (sprintf ("%s/bin:/usr/bin:/bin:/usr/local/bin") (installDir()))
  EnvironmentHelper.setEnvironVar "CFLAGS" (sprintf ("-I%s/include") (installDir()))
  EnvironmentHelper.setEnvironVar "LD_LIBRARY_PATH" (sprintf ("%s/lib") (installDir()))
  EnvironmentHelper.setEnvironVar "LDFLAGS" (sprintf ("-L%s/lib") (installDir()))
  EnvironmentHelper.setEnvironVar "C_INCLUDE_PATH" (sprintf ("%s/include") (installDir()))
  EnvironmentHelper.setEnvironVar "ACLOCAL_FLAGS" (sprintf ("-I%s/share/aclocal") (installDir()))
  EnvironmentHelper.setEnvironVar "PKG_CONFIG_PATH" (sprintf ("%s/lib/pkgconfig:%s/share/pkgconfig") (installDir()) (installDir()))
  EnvironmentHelper.setEnvironVar "XDG_CONFIG_DIRS" (sprintf ("%s/etc/xdg") (installDir()))
  EnvironmentHelper.setEnvironVar "XDG_CONFIG_HOME" "$HOME/.config"
  EnvironmentHelper.setEnvironVar "XDG_DATA_DIRS" (sprintf ("%s/share") (installDir()))
  EnvironmentHelper.setEnvironVar "JHBUILD_PREFIX" (installDir())

  pkg
  |> download
  |> extract
  |> build
  |> ignore

// Targets
// --------------------------------------------------------
Target "prep" <| fun _ ->
  trace("prep")

Target "autoconf" <| fun _ ->
  defSource Gnu "autoconf" "2.69"
  |> forPackage "autoconf"
  |> startBuild

Target "automake" <| fun _ ->
  defSource Gnu "automake" "1.13.4"
  |> forPackage "autoconf"
  |> startBuild

Target "libtool" <| fun _ ->
  defSource Gnu "libtool" "2.4.2"
  |> forPackage "autoconf"
  |> startBuild

Target "pkgconfig" <| fun _ ->
  defSource FreeDesktop "pkg-config" "0.27"
  |> forPackage "pkg-config"
  |> withConfigFlags "--with-internal-glib"
  |> startBuild

Target "gettext" <| fun _ ->
  defSource Gnu "gettext" "0.18.2"
  |> forPackage "gettext"
  |> startBuild

Target "freetype" <| fun _ ->
  ensureDirectory (Path.Combine(installDir(), "include", "freetype2", "freetype", "cache"))
  ensureDirectory (Path.Combine(installDir(), "include", "freetype2", "freetype", "internal"))

  defSource SourceForge "freetype" "2.5.0.1"
  |> forPackage "freetype"
  |> startBuild

Target "libffi" <| fun _ ->
  let version = "3.0.13"
  let url = sprintf "ftp://sourceware.org/pub/libffi/libffi-%s.tar.gz" version

  defCustomSource url "libffi" version
  |> forPackage "libffi"
  |> withConfigFlags "--srcdir=/Users/cody/xam/gtkmacbuild/root/libffi-3.0.13" // TODO
  |> startBuild

Target "glib" <| fun _ ->
  defSource GnomeXz "glib" "2.36.4"
  |> forPackage "glib"
  |> withConfigFlags "--disable-compile-warnings"
  |> startBuild

Target "harfbuzz" <| fun _ ->
  trace("harfbuzz")

Target "atk" <| fun _ ->
  defSource GnomeXz "atk" "2.8.0"
  |> forPackage "atk"
  |> startBuild

Target "gdk-pixbuf" <| fun _ ->
  defSource GnomeXz "gdk-pixbuf" "2.28.2"
  |> forPackage "gdk-pixbuf"
  |> startBuild

Target "fontconfig" <| fun _ ->
  let version = "2.10.2"
  let url = sprintf "http://www.fontconfig.org/release/fontconfig-%s.tar.gz" version

  defCustomSource url "fontconfig" version
  |> forPackage "fontconfig"
  |> withConfigFlags "--disable-docs"
  |> startBuild

Target "pixman" <| fun _ ->
  defSource Cairo "pixman" "0.30.0"
  |> forPackage "pixman"
  |> startBuild

Target "cairo" <| fun _ ->
  defSource CairoXz "cairo" "1.12.14"
  |> forPackage "cairo"
  |> withConfigFlags "--enable-pdf --enable-quartz --enable-quartz-font --enable-quartz-image --disable-xlib --without-x"
  |> startBuild

Target "pango" <| fun _ ->
  defSource GnomeXz "pango" "1.35.0"
  |> forPackage "pango"
  |> withConfigFlags "--without-x"
  |> startBuild

Target "gtk-osx-docbook" <| fun _ ->
  gitSource "git@github.com:jralls/gtk-osx-docbook.git" "master" "058d8a2f3f0d37de00b8e9ac78f633706deb5e22"
  |> forPackage "docbook"
  |> startBuild

Target "gtk-doc" <| fun _ ->
  defSource GnomeXz "gtk-doc" "1.18"
  |> forPackage "gtk-doc"
  |> startBuild

Target "gtk" <| fun _ ->
  rm (Path.Combine(installDir(), "share", "aclocal", "glib-gettext.m4"))

  gitSource "git@github.com:bratsche/gtk.git" "xamarin-mac" "HEAD"
  |> forPackage "gtk"
  |> configurePackage Autogen
  |> withConfigFlags "--with-gdktarget=quartz --disable-man"
  |> startBuild

Target "xamarin-gtk-theme" <| fun _ ->
  trace("xamarin-gtk-theme")

Target "libpng" <| fun _ ->
  defSource SourceForge "libpng" "1.4.12"
  |> forPackage "libpng"
  |> startBuild

Target "libjpeg" <| fun _ ->
  defCustomSource "http://www.ijg.org/files/jpegsrc.v8.tar.gz" "jpeg" "8"
  |> forPackage "libjpeg"
  |> startBuild

Target "libtiff" <| fun _ ->
  let version = "4.0.8"
  let url = sprintf "http://download.osgeo.org/libtiff/tiff-%s.tar.gz" version

  defCustomSource url "tiff" version
  |> forPackage "tiff"
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
"gdk-pixbuf" <== ["glib"; "libpng"; "libtiff"; "libjpeg"]
"glib" <== ["libffi"]
"gtk" <== ["atk"; "gdk-pixbuf"; "pango"]
"harfbuzz" <== ["freetype"; "glib"]
"pango" <== ["cairo"; "harfbuzz"]
"pixman" <== ["libpng"; "libjpeg"; "libtiff"; "libgif"]
"xamarin-gtk-theme" <== ["gtk"]
"BuildAll" <== ["prep"; "gtk"; "xamarin-gtk-theme"]

RunTargetOrDefault "BuildAll"
