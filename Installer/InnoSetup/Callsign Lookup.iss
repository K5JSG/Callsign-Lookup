; ===================================================================
;  Callsign Lookup - installer
;
;  Build with Inno Setup 6 or newer (run from the repo root):
;      iscc "Installer\InnoSetup\Callsign Lookup.iss"
;
;  Expects the published self-contained single-file exe and its Data
;  folder at:
;      publish\Callsign Lookup.exe
;      publish\Data\*.json
;  (build.ps1, at the repo root, puts them there and runs this)
; ===================================================================

#define MyAppName "Callsign Lookup"
#define MyAppPublisher "K5JSG"
#define MyAppURL "https://github.com/K5JSG/Callsign-Lookup"
#define MyAppExeName "Callsign Lookup.exe"

; Overridable from the command line: iscc /DMyAppVersion=1.0.0 ...
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
; Keep this GUID stable forever: it is how Windows recognises an upgrade of
; the same product rather than a second installation.
AppId={{8D2904C4-94D6-4263-9708-7AE338F1C5A8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
LicenseFile=..\..\License.txt

; Writing to Program Files needs admin - the app itself does not require
; elevation to run (its settings live in %LocalAppData%), just to install.
PrivilegesRequired=admin

OutputDir=..\..\dist
OutputBaseFilename={#MyAppName} Setup {#MyAppVersion}
SetupIconFile=..\..\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 1809 or newer
MinVersion=10.0.17763

; Offer to shut the app down instead of demanding a reboot
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; \
    GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\publish\Data\*.json"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "..\..\License.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; \
    Flags: postinstall nowait skipifsilent

[Code]

// Every update is a clean uninstall + reinstall rather than an in-place
// overwrite, so Installed Apps only ever shows one entry and no file dropped
// by an older version is left behind in the program folder. User settings
// (the QRZ login) live in %LocalAppData% and are never touched.

const
  UninstallKeyRoot = 'Software\Microsoft\Windows\CurrentVersion\Uninstall';

// Only ever wipes a folder that is clearly this app's own (named after the
// product), never some general-purpose folder the user may have picked on
// the directory page.
procedure DeleteAppFolder(Dir: String);
begin
  Dir := RemoveBackslashUnlessRoot(Dir);
  if (Dir <> '') and DirExists(Dir) and (CompareText(ExtractFileName(Dir), '{#MyAppName}') = 0) then
  begin
    DelTree(Dir, True, True, True);
    // The K5JSG publisher folder above it - RemoveDir only succeeds if empty
    RemoveDir(ExtractFileDir(Dir));
  end;
end;

// An Inno uninstaller re-launches itself from %TEMP% and deletes its own
// unins*.exe as the very last step, so this confirms it has really finished
// before the new files go in.
procedure WaitForFileGone(const FileName: String; TimeoutMs: Integer);
begin
  while FileExists(FileName) and (TimeoutMs > 0) do
  begin
    Sleep(250);
    TimeoutMs := TimeoutMs - 250;
  end;
end;

// Silently uninstalls every installed copy of this app registered under
// RootKey. Returns an error message, or '' if everything went fine.
function UninstallOldVersions(RootKey: Integer): String;
var
  Names: TArrayOfString;
  I, ResultCode: Integer;
  Key, DisplayName, Publisher, Uninstaller: String;
begin
  Result := '';
  if not RegGetSubkeyNames(RootKey, UninstallKeyRoot, Names) then Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    Key := UninstallKeyRoot + '\' + Names[I];
    if RegQueryStringValue(RootKey, Key, 'DisplayName', DisplayName) and
       (CompareText(DisplayName, '{#MyAppName}') = 0) and
       RegQueryStringValue(RootKey, Key, 'Publisher', Publisher) and
       (CompareText(Publisher, '{#MyAppPublisher}') = 0) and
       RegQueryStringValue(RootKey, Key, 'UninstallString', Uninstaller) then
    begin
      Uninstaller := RemoveQuotes(Uninstaller);
      if FileExists(Uninstaller) then
      begin
        if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
                    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          ResultCode := -1;
        if ResultCode <> 0 then
        begin
          Result := Format('%s could not be removed automatically (error %d).', [DisplayName, ResultCode]);
          Exit;
        end;
        WaitForFileGone(Uninstaller, 60000);
      end;
      DeleteAppFolder(ExtractFileDir(Uninstaller));
      // An entry whose uninstaller is missing (folder deleted by hand)
      // would otherwise linger in Installed Apps forever.
      if RegKeyExists(RootKey, Key) then
        RegDeleteKeyIncludingSubkeys(RootKey, Key);
    end;
  end;
end;

// Stop a running instance before installing or uninstalling, otherwise the
// exe is locked and the file copy fails.
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM "{#MyAppExeName}" >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();

  Result := UninstallOldVersions(HKLM64);
  if Result = '' then Result := UninstallOldVersions(HKLM32);
  if Result = '' then Result := UninstallOldVersions(HKCU);

  if Result <> '' then
  begin
    Result := Result + #13#10#13#10 +
      'Please uninstall it from Settings > Apps > Installed apps, then run this setup again.';
    Exit;
  end;

  DeleteAppFolder(ExpandConstant('{app}'));
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    DeleteAppFolder(ExpandConstant('{app}'));
end;
