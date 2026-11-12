#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif
#ifndef SourceDir
  #define SourceDir "publish"
#endif
#ifndef OutputDir
  #define OutputDir "release"
#endif

[Setup]
AppId={{B35B699D-F0EA-4F62-9E00-38BF10117825}
AppName=Converter+
AppVersion={#AppVersion}
AppPublisher=fiol4ik
DefaultDirName={localappdata}\Programs\ConverterPlus
DefaultGroupName=Converter+
UninstallDisplayIcon={app}\ConverterPlus.exe
OutputDir={#OutputDir}
OutputBaseFilename=ConverterPlus-Setup-{#AppVersion}
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
DisableProgramGroupPage=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Converter+"; Filename: "{app}\ConverterPlus.exe"
Name: "{autodesktop}\Converter+"; Filename: "{app}\ConverterPlus.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ConverterPlus.exe"; Description: "Launch Converter+"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  MarkerPath: string;
  SettingsPath: string;
  MarkerContent: string;
begin
  if CurStep = ssPostInstall then
  begin
    MarkerPath := ExpandConstant('{userappdata}\ConverterPlus\onboarding.pending');
    SettingsPath := ExpandConstant('{userappdata}\ConverterPlus\settings.json');
    if FileExists(SettingsPath) then
      MarkerContent := 'reinstall'
    else
      MarkerContent := 'first';
    ForceDirectories(ExtractFileDir(MarkerPath));
    SaveStringToFile(MarkerPath, MarkerContent, False);
  end;
end;
