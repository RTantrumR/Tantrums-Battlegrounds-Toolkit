; Tantrum's Battlegrounds Toolkit — per-user setup of the standalone program.
;
; TantrumsToolkitStandalone.exe and the plugin assembly it hosts go to
; %LOCALAPPDATA%\Programs\TantrumsBattlegroundsToolkit, with Start menu and desktop shortcuts; its data
; lives in %APPDATA%\TantrumsBattlegroundsToolkit (the exe copies the old plugin's folder there on its
; first run). A page makes the player choose how it starts — with Windows (the --autostart watcher,
; idle in the tray until Hearthstone starts: HKCU\...\Run) or by hand — with no default
; (/AUTOSTART=yes|no for a silent run). A running standalone is asked to close (the named event
; TantrumsBattlegroundsToolkit.Quit, which Standalone\Shared.cs listens for) before its files are replaced.
; Built by installer\build.ps1, which stages the Release build into installer\stage and passes the
; version as /DAppVersion.
;
; The HDT plugin is no longer installed (HDT ends plugins on 2026-11-30; the "Install as HDT plugin"
; choice was removed 2026-11-24). An old plugin install still switched on is switched OFF in HDT's
; plugins.xml (IsEnabled=false; HDT is asked to close first, or its shutdown would write it back), so
; the toolkit never runs twice. Its files stay, so switching back is one click in HDT.
;
; Languages. The wizard runs in the language Windows is set to when it has that one (English
; otherwise — Russian and Ukrainian systems included); Inno's own "Select Setup Language" box is off.
; A small selector at the top right of every page before the install switches it: Inno cannot change
; a running wizard's language, so Setup starts itself again with /LANG=, on the same page, carrying
; the toolkit choice if the user made one (/TOOLKITLANG=, /STARTPAGE=). Its own lines live in
; installer\i18n\<lang>.json; build.ps1 turns them into out\messages.iss, #included below. The first
; page asks which language the TOOLKIT uses — by default the wizard's language (es-MX and the other
; Latin American Spanish systems get es-mx, Taiwan/Hong Kong/Macau Chinese get zh-tw), on an
; upgrade the language already set; the Ready page shows it again. The choice reaches the plugin as setup-language.txt in its data
; folder, which the plugin applies once and deletes; the wizard never writes config.xml, because
; the toolkit's first-run defaults and its welcome marker both depend on it not existing.
; /TOOLKITLANG=<code> preselects (silent installs too); "follow" = same as HDT.
;
; Testing: /DIR=<temp> installs the program into a throwaway folder, /DATADIR= the data, /HDTDATA= HDT's
; data folder (plugins.xml). A /DIR test is not fully throwaway: it rewrites the per-user Apps & features
; entry (HKCU\...\Uninstall\{9B1E4C7A-5D62-4F0B-8E3C-2A7F6D9C1B45}_is1) to point at the test folder —
; put App Path, InstallLocation, DisplayIcon, (Quiet)UninstallString and DisplayVersion back after, and
; never run the test folder's uninstaller (it deletes the real entry). It also points the REAL Start menu
; and desktop shortcuts at the test folder, and /AUTOSTART=no deletes the real HKCU Run value (seen
; 2026-11-24): put those back too (reg export the Uninstall key and the Run key first).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppDisplayName "Tantrum's Battlegrounds Toolkit"

[Setup]
AppId={{9B1E4C7A-5D62-4F0B-8E3C-2A7F6D9C1B45}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher=hsbg.cards
AppPublisherURL=https://github.com/RTantrumR/Tantrums-Battlegrounds-Toolkit
AppSupportURL=https://github.com/RTantrumR/Tantrums-Battlegrounds-Toolkit/issues
AppUpdatesURL=https://github.com/RTantrumR/Tantrums-Battlegrounds-Toolkit/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany=hsbg.cards
VersionInfoCopyright=Copyright © 2026 TantrumUA
DefaultDirName={localappdata}\Programs\TantrumsBattlegroundsToolkit
DisableDirPage=yes
UsePreviousAppDir=no
DirExistsWarning=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UninstallFilesDir={code:DataDir}\uninstall
UninstallDisplayName={code:UninstallName}
UninstallDisplayIcon={code:UninstallIcon}
OutputDir=out
OutputBaseFilename=TantrumsBattlegroundsToolkit-Setup-{#AppVersion}
; Stored, not compressed: a packed payload reads as obfuscated to heuristic scanners (the 1.0.0
; Setup drew Skyhigh's BehavesLike.Win32.ObfuscatedPoly on VirusTotal).
Compression=none
; Signed only when installeruild.ps1 gets -SignPfx (it then defines Sign and the "toolkit" tool),
; which signs the Setup and the uninstaller it writes.
#ifdef Sign
SignTool=toolkit
#endif
WizardStyle=modern
; The language page holds an intro, a row and two columns of seven: a tenth more room than the default.
WizardSizePercent=110
; The wizard's language comes from Windows, or from the selector at the top right of the wizard.
ShowLanguageDialog=no
CloseApplications=no

[Languages]
; English first: it is what a Windows language without its own entry gets.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "thai"; MessagesFile: "compiler:Languages\Thai.isl"
; Not shipped with Inno Setup 6.7: the official translations from jrsoftware/issrc (Files\Languages).
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "languages\ChineseTraditional.isl"

; Our own wizard texts in every language above (Setup creates no shortcuts, so Inno shows
; FinishedLabelNoIcons; FinishedLabel is kept in step there).
#include "out\messages.iss"

[Files]
; The standalone: the exe, the plugin assembly it hosts, the bundled libraries, data\ and the icon.
Source: "stage\standalone\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; The language page's flags (rendered from the plugin's own Ui\Flags.cs by build.ps1), unpacked
; to {tmp} for the page only.
Source: "out\flags\*.bmp"; Flags: dontcopy

[InstallDelete]
; The installer replaces the whole set: a DLL or data file that a newer version no longer ships
; must not linger from an older one.
Type: files; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\data"

[Icons]
Name: "{userprograms}\{#AppDisplayName}"; Filename: "{app}\TantrumsToolkitStandalone.exe"; WorkingDir: "{app}"
; Always, in either start mode (owner, 2026-11-19): a closed toolkit must be easy to find again.
Name: "{userdesktop}\{#AppDisplayName}"; Filename: "{app}\TantrumsToolkitStandalone.exe"; WorkingDir: "{app}"

[Run]
; The standalone: with autostart, the watcher (it brings the toolkit up when Hearthstone starts);
; otherwise the toolkit itself.
Filename: "{app}\TantrumsToolkitStandalone.exe"; Parameters: "{code:ToolkitStartArgs}"; WorkingDir: "{app}"; Description: "{cm:RunToolkit}"; Flags: postinstall nowait skipifsilent

[Code]
const
  WM_CLOSE = $0010;
  AppName = '{#StringChange(AppDisplayName, "'", "''")}';   // the display name, apostrophe escaped
  HdtProcessName = 'HearthstoneDeckTracker.exe';
  HdtWindowTitle = 'Hearthstone Deck Tracker';
  PluginFileName = 'Plugins/HsbgCardLookup/HsbgCardLookup.dll';
  // Shared with Standalone\Shared.cs — change both together.
  StandaloneExe = 'TantrumsToolkitStandalone.exe';
  QuitEventName = 'TantrumsBattlegroundsToolkit.Quit';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'TantrumsBattlegroundsToolkit';

var
  LangPage: TWizardPage;
  LangRadios: array of TNewRadioButton;
  LangCodes: TArrayOfString;   // LangRadios[i] -> the plugin's language code ('' = same as HDT)
  LangNames: TArrayOfString;   // LangRadios[i] -> what the row says, for the Ready page
  CurrentLang: String;         // the plugin's Language setting before this install; '?' = no config yet
  LangPreselected: Boolean;    // PreselectLanguage has run
  LangTouched: Boolean;        // the user picked a toolkit language on the page (carried over a restart)
  Selecting: Boolean;          // SelectLanguage is setting a radio: not a user pick
  WizardLangCombo: TNewComboBox;
  WizardLangFlag: TBitmapImage;
  WizardLangNames: TArrayOfString;   // WizardLangCombo item i -> Inno language name
  Restarting: Boolean;         // closing this run because Setup was started again in another language

  AutoPage: TWizardPage;
  AutoYesRadio, AutoNoRadio: TNewRadioButton;
  DesktopCheck: TNewCheckBox;
  SharePage: TWizardPage;
  ShareCheck: TNewCheckBox;
  PluginDisabled: Boolean;      // this run switched an old HDT plugin install off

// ---------------------------------------------------------------------------------------------
// Where the toolkit keeps its files
// ---------------------------------------------------------------------------------------------

function StandaloneDefaultDir: String;
begin
  Result := ExpandConstant('{localappdata}\Programs\TantrumsBattlegroundsToolkit');
end;

// HDT's data folder, where plugins.xml says whether an old plugin install is switched on.
function HdtDataRoot: String;
begin
  Result := ExpandConstant('{param:HDTDATA|{userappdata}\HearthstoneDeckTracker}');
end;

function StandaloneDataDir: String;
begin
  Result := ExpandConstant('{param:DATADIR|{userappdata}\TantrumsBattlegroundsToolkit}');
end;

function DataDir(Param: String): String;
begin
  Result := StandaloneDataDir;
end;

function UninstallName(Param: String): String;
begin
  Result := AppName;
end;

function UninstallIcon(Param: String): String;
begin
  Result := ExpandConstant('{app}\' + StandaloneExe);
end;

function WantAutostart: Boolean;
begin
  Result := (AutoYesRadio <> nil) and AutoYesRadio.Checked;
end;

function ToolkitStartArgs(Param: String): String;
begin
  if WantAutostart then Result := '--autostart' else Result := '';
end;

function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

// ---------------------------------------------------------------------------------------------
// The "Toolkit language" page
// ---------------------------------------------------------------------------------------------

// The plugin's Language setting as config.xml holds it ('' = same as HDT), or '?' when the plugin
// has no settings file yet (a fresh install). A plain text search: the element is a single line
// written by .NET's XmlSerializer, <Language>de</Language> or <Language /> when empty.
function ReadCurrentLanguage: String;
var
  Xml: AnsiString;
  Text: String;
  I, J: Integer;
begin
  Result := '?';
  // The standalone's own config.xml, else the old plugin's (what the standalone copies on its first run).
  Text := '';
  if LoadStringFromFile(StandaloneDataDir + '\config.xml', Xml) then Text := String(Xml)
  else
  begin
    if not LoadStringFromFile(HdtDataRoot + '\HsbgCardLookup\config.xml', Xml) then Exit;
    Text := String(Xml);
  end;
  if Text = '' then Exit;
  Result := '';
  I := Pos('<Language>', Text);
  if I = 0 then Exit;
  I := I + Length('<Language>');
  J := Pos('</Language>', Copy(Text, I, 64));
  if J > 0 then
    Result := Trim(Copy(Text, I, J - 1));
end;

// Windows' UI language split into its primary language and sublanguage (LANGID bits).
function UiPrimary: Integer;
begin
  Result := GetUILanguage and $3FF;
end;

function UiSub: Integer;
begin
  Result := GetUILanguage shr 10;
end;

// The toolkit language that matches the wizard's language. The wizard has one Spanish and picks
// the Chinese entry by exact LANGID only, so the variant comes from Windows itself when Windows is
// set to that language: Spanish other than Spain's -> es-mx; Chinese of Taiwan (1), Hong Kong (3)
// or Macau (5) -> zh-tw, of the PRC (2) or Singapore (4) -> zh-cn.
function DefaultToolkitLanguage: String;
var
  L: String;
begin
  L := ActiveLanguage;
  if L = 'german' then Result := 'de'
  else if L = 'spanish' then
  begin
    Result := 'es';
    if (UiPrimary = $0A) and (UiSub <> 1) and (UiSub <> 3) then Result := 'es-mx';
  end
  else if L = 'french' then Result := 'fr'
  else if L = 'italian' then Result := 'it'
  else if L = 'japanese' then Result := 'ja'
  else if L = 'korean' then Result := 'ko'
  else if L = 'polish' then Result := 'pl'
  else if L = 'brazilianportuguese' then Result := 'pt'
  else if L = 'thai' then Result := 'th'
  else if (L = 'chinesesimplified') or (L = 'chinesetraditional') then
  begin
    if L = 'chinesetraditional' then Result := 'zh-tw' else Result := 'zh-cn';
    if UiPrimary = $04 then
      if (UiSub = 1) or (UiSub = 3) or (UiSub = 5) then Result := 'zh-tw' else Result := 'zh-cn';
  end
  else Result := 'en';
end;

// The flag size nearest this DPI, drawn by build.ps1 at 22/28/33/44 px wide (100-200 %).
function FlagWidth: Integer;
begin
  Result := ScaleX(22);
  if Result <= 24 then Result := 22
  else if Result <= 30 then Result := 28
  else if Result <= 38 then Result := 33
  else Result := 44;
end;

function LoadMark(const FileName: String; Parent: TWinControl; Left, Top: Integer): TBitmapImage;
begin
  ExtractTemporaryFile(FileName);
  Result := TBitmapImage.Create(LangPage);
  Result.Parent := Parent;
  Result.Bitmap.LoadFromFile(ExpandConstant('{tmp}\' + FileName));
  Result.AutoSize := True;
  Result.Left := Left;
  Result.Top := Top;
end;

// A click on a row's flag or words picks that row, as on the radio itself.
procedure LangRowClick(Sender: TObject);
begin
  LangRadios[TComponent(Sender).Tag].Checked := True;
end;

procedure LangRadioClick(Sender: TObject);
begin
  if not Selecting then LangTouched := True;
end;

// One row: radio, mark, name in its own words, and the locale tag in grey — the plugin's own
// language page in miniature (Settings > Language).
procedure AddLang(const Code, Name, LocaleTag, MarkFile: String; Left, Top, RowH: Integer);
var
  N: Integer;
  Radio: TNewRadioButton;
  Mark: TBitmapImage;
  NameText, TagText: TNewStaticText;
begin
  N := GetArrayLength(LangCodes);
  SetArrayLength(LangCodes, N + 1);
  SetArrayLength(LangNames, N + 1);
  SetArrayLength(LangRadios, N + 1);
  LangCodes[N] := Code;
  if LocaleTag <> '' then LangNames[N] := Name + ' (' + LocaleTag + ')' else LangNames[N] := Name;

  Radio := TNewRadioButton.Create(LangPage);
  Radio.Parent := LangPage.Surface;
  Radio.Left := Left;
  Radio.Width := ScaleX(17);
  Radio.Height := RowH;
  Radio.Top := Top;
  Radio.OnClick := @LangRadioClick;
  LangRadios[N] := Radio;

  Mark := LoadMark(MarkFile, LangPage.Surface, Left + ScaleX(19), Top);
  Mark.Top := Top + (RowH - Mark.Height) div 2;
  Mark.Tag := N;
  Mark.OnClick := @LangRowClick;

  NameText := TNewStaticText.Create(LangPage);
  NameText.Parent := LangPage.Surface;
  NameText.Caption := Name;
  NameText.Left := Mark.Left + Mark.Width + ScaleX(6);
  NameText.Top := Top + (RowH - NameText.Height) div 2;
  NameText.Tag := N;
  NameText.OnClick := @LangRowClick;

  if LocaleTag <> '' then
  begin
    TagText := TNewStaticText.Create(LangPage);
    TagText.Parent := LangPage.Surface;
    TagText.Caption := LocaleTag;
    TagText.Font.Color := clGrayText;
    TagText.Left := NameText.Left + NameText.Width + ScaleX(6);
    TagText.Top := NameText.Top;
    TagText.Tag := N;
    TagText.OnClick := @LangRowClick;
  end;
end;

function ChosenIndex: Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to GetArrayLength(LangRadios) - 1 do
    if LangRadios[I].Checked then Result := I;
end;

// The code the page (or /TOOLKITLANG) settles on.
function ChosenLanguage: String;
var
  I: Integer;
begin
  Result := '';
  I := ChosenIndex;
  if I >= 0 then Result := LangCodes[I];
end;

function ChosenLanguageName: String;
var
  I: Integer;
begin
  Result := '';
  I := ChosenIndex;
  if I >= 0 then Result := LangNames[I];
end;

procedure SelectLanguage(const Code: String);
var
  I: Integer;
begin
  for I := 0 to GetArrayLength(LangCodes) - 1 do
    if CompareText(LangCodes[I], Code) = 0 then
    begin
      Selecting := True;
      LangRadios[I].Checked := True;
      Selecting := False;
      Exit;
    end;
  // A code this list doesn't know (a language added later, a typo in config.xml) -> English.
  SelectLanguage('en');
end;

procedure CreateLanguagePage;
var
  Intro: TNewStaticText;
  W, RowH, Top, ColW: Integer;
  F: String;
begin
  // The first page (Inno 6 shows no welcome page): the toolkit's language is the first question.
  LangPage := CreateCustomPage(wpWelcome, CustomMessage('LangPageCaption'), CustomMessage('LangPageDescription'));

  Intro := TNewStaticText.Create(LangPage);
  Intro.Parent := LangPage.Surface;
  Intro.AutoSize := False;
  Intro.WordWrap := True;
  Intro.Left := 0;
  Intro.Top := 0;
  Intro.Width := LangPage.SurfaceWidth;
  Intro.Caption := CustomMessage('LangIntro');
  WizardForm.AdjustLabelHeight(Intro);

  W := FlagWidth;
  F := '_' + IntToStr(W) + '.bmp';
  RowH := ScaleY(23);
  ColW := LangPage.SurfaceWidth div 2;
  Top := Intro.Top + Intro.Height + ScaleY(8);

  // "Same as HDT" across the top, then the languages in two columns, each in its own words with
  // the flag and tag the plugin and the site show.
  AddLang('', CustomMessage('LangSameAsHdt'), '', 'globe' + F, 0, Top, RowH);
  Top := Top + RowH + ScaleY(4);
  AddLang('en', 'English', 'en-US', 'flag_en' + F, 0, Top + 0 * RowH, RowH);
  AddLang('de', 'Deutsch', 'de-DE', 'flag_de' + F, 0, Top + 1 * RowH, RowH);
  AddLang('es', 'Español (España)', 'es-ES', 'flag_es' + F, 0, Top + 2 * RowH, RowH);
  AddLang('es-mx', 'Español (Latinoamérica)', 'es-MX', 'flag_es-mx' + F, 0, Top + 3 * RowH, RowH);
  AddLang('fr', 'Français', 'fr-FR', 'flag_fr' + F, 0, Top + 4 * RowH, RowH);
  AddLang('it', 'Italiano', 'it-IT', 'flag_it' + F, 0, Top + 5 * RowH, RowH);
  AddLang('ja', '日本語', 'ja-JP', 'flag_ja' + F, 0, Top + 6 * RowH, RowH);
  AddLang('ko', '한국어', 'ko-KR', 'flag_ko' + F, ColW, Top + 0 * RowH, RowH);
  AddLang('pl', 'Polski', 'pl-PL', 'flag_pl' + F, ColW, Top + 1 * RowH, RowH);
  AddLang('pt', 'Português (Brasil)', 'pt-BR', 'flag_pt' + F, ColW, Top + 2 * RowH, RowH);
  AddLang('th', 'ไทย', 'th-TH', 'flag_th' + F, ColW, Top + 3 * RowH, RowH);
  AddLang('zh-cn', '简体中文', 'zh-CN', 'flag_zh-cn' + F, ColW, Top + 4 * RowH, RowH);
  AddLang('zh-tw', '繁體中文', 'zh-TW', 'flag_zh-tw' + F, ColW, Top + 5 * RowH, RowH);
end;

// Preselect: /TOOLKITLANG, else the setting already there, else the wizard's language. Once, right
// after the page is built; the later calls (Ready page, install) are for safety only.
procedure PreselectLanguage;
var
  Wanted: String;
begin
  if LangPreselected then Exit;
  LangPreselected := True;
  CurrentLang := ReadCurrentLanguage;
  Wanted := ExpandConstant('{param:TOOLKITLANG|}');
  if CompareText(Wanted, 'follow') = 0 then
    SelectLanguage('')
  else if Wanted <> '' then
    SelectLanguage(Wanted)
  else if CurrentLang <> '?' then
    SelectLanguage(CurrentLang)
  else
    SelectLanguage(DefaultToolkitLanguage);
  Log('Toolkit language: current=' + CurrentLang + ' default=' + DefaultToolkitLanguage +
    ' preselected=' + ChosenLanguage + ' (wizard: ' + ActiveLanguage + ', UI LANGID $' + Format('%.4x', [GetUILanguage]) + ')');
end;

// ---------------------------------------------------------------------------------------------
// The wizard-language selector (top right of every page before the install)
// ---------------------------------------------------------------------------------------------

// Inno language name -> the plugin's code, for the flag next to the selector.
function FlagCodeOf(const InnoName: String): String;
begin
  if InnoName = 'german' then Result := 'de'
  else if InnoName = 'spanish' then Result := 'es'
  else if InnoName = 'french' then Result := 'fr'
  else if InnoName = 'italian' then Result := 'it'
  else if InnoName = 'japanese' then Result := 'ja'
  else if InnoName = 'korean' then Result := 'ko'
  else if InnoName = 'polish' then Result := 'pl'
  else if InnoName = 'brazilianportuguese' then Result := 'pt'
  else if InnoName = 'thai' then Result := 'th'
  else if InnoName = 'chinesesimplified' then Result := 'zh-cn'
  else if InnoName = 'chinesetraditional' then Result := 'zh-tw'
  else Result := 'en';
end;

procedure AddWizardLang(const InnoName, Native: String);
var
  N: Integer;
begin
  N := GetArrayLength(WizardLangNames);
  SetArrayLength(WizardLangNames, N + 1);
  WizardLangNames[N] := InnoName;
  WizardLangCombo.Items.Add(Native);
  if InnoName = ActiveLanguage then WizardLangCombo.ItemIndex := N;
end;

// The command line of this run without the parameters a restart sets itself. Quoted arguments
// (paths with spaces) stay whole.
function CarriedParams: String;
var
  Tail, Arg, Upper: String;
  I: Integer;
  InQuote: Boolean;
begin
  Result := '';
  Tail := GetCmdTail + ' ';
  Arg := '';
  InQuote := False;
  for I := 1 to Length(Tail) do
  begin
    if Tail[I] = '"' then InQuote := not InQuote;
    if (Tail[I] = ' ') and not InQuote then
    begin
      if Arg <> '' then
      begin
        Upper := Uppercase(RemoveQuotes(Arg));
        if (Pos('/LANG=', Upper) <> 1) and (Pos('/TOOLKITLANG=', Upper) <> 1) and (Pos('/STARTPAGE=', Upper) <> 1)
           and (Pos('/MODE=', Upper) <> 1) and (Pos('/AUTOSTART=', Upper) <> 1) and (Pos('/DESKTOP=', Upper) <> 1)
           and (Pos('/SHAREDATA=', Upper) <> 1)
           and (Pos('/SL5=', Upper) <> 1) and (Pos('/SPAWNWND=', Upper) <> 1) and (Pos('/NOTIFYWND=', Upper) <> 1) then
          Result := Result + ' ' + Arg;
      end;
      Arg := '';
    end
    else
      Arg := Arg + Tail[I];
  end;
end;

// Start Setup again in another language, on the page the user is on, then close this run quietly.
procedure WizardLangChange(Sender: TObject);
var
  NewLang, Params, Page: String;
  Code: Integer;
  Started: Boolean;
begin
  if WizardLangCombo.ItemIndex < 0 then Exit;
  NewLang := WizardLangNames[WizardLangCombo.ItemIndex];
  if NewLang = ActiveLanguage then Exit;
  if WizardForm.CurPageID = AutoPage.ID then Page := 'auto'
  else if WizardForm.CurPageID = SharePage.ID then Page := 'share'
  else if WizardForm.CurPageID = wpReady then Page := 'ready'
  else Page := 'lang';
  Params := CarriedParams + ' /LANG=' + NewLang + ' /STARTPAGE=' + Page;
  if AutoYesRadio.Checked then Params := Params + ' /AUTOSTART=yes'
  else if AutoNoRadio.Checked then Params := Params + ' /AUTOSTART=no';
  if not DesktopCheck.Checked then Params := Params + ' /DESKTOP=no';
  if ShareCheck.Checked then Params := Params + ' /SHAREDATA=yes' else Params := Params + ' /SHAREDATA=no';
  // The toolkit choice travels only when the user made it (or it came from an upgrade / the
  // parameter); otherwise the new wizard language picks the default afresh.
  if LangTouched or (CurrentLang <> '?') or (ExpandConstant('{param:TOOLKITLANG|}') <> '') then
  begin
    if ChosenLanguage = '' then Params := Params + ' /TOOLKITLANG=follow'
    else Params := Params + ' /TOOLKITLANG=' + ChosenLanguage;
  end;
  Log('Wizard language -> ' + NewLang + ': starting Setup again with' + Params);
  // Starting {srcexe} straight from here is refused with error 5 (access denied), by ShellExec and
  // Exec alike, though a second copy starts fine from outside. Try it, then through cmd's start.
  Started := Exec(ExpandConstant('{srcexe}'), Params, ExtractFileDir(ExpandConstant('{srcexe}')), SW_SHOWNORMAL, ewNoWait, Code);
  if not Started then
  begin
    Log('Direct start refused: ' + SysErrorMessage(Code) + ' (' + IntToStr(Code) + '); trying cmd /c start');
    Started := Exec(ExpandConstant('{cmd}'), '/c start "" "' + ExpandConstant('{srcexe}') + '"' + Params, '', SW_HIDE, ewNoWait, Code);
  end;
  if not Started then
  begin
    Log('Could not start Setup again: ' + SysErrorMessage(Code) + ' (' + IntToStr(Code) + ')');
    // Stay in this language and say so on the selector.
    for Code := 0 to GetArrayLength(WizardLangNames) - 1 do
      if WizardLangNames[Code] = ActiveLanguage then WizardLangCombo.ItemIndex := Code;
    Exit;
  end;
  Restarting := True;
  WizardForm.Close;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Restarting then Confirm := False;
end;

// In the band at the top of the wizard, where Inno's small picture sits: this window's flag and a
// list of the wizard's languages in their own words.
procedure CreateWizardLangSelector;
var
  W: Integer;
begin
  WizardForm.WizardSmallBitmapImage.Visible := False;

  WizardLangCombo := TNewComboBox.Create(WizardForm);
  WizardLangCombo.Parent := WizardForm.MainPanel;
  WizardLangCombo.Style := csDropDownList;
  WizardLangCombo.Width := ScaleX(150);
  WizardLangCombo.Left := WizardForm.MainPanel.ClientWidth - WizardLangCombo.Width - ScaleX(12);
  WizardLangCombo.Top := (WizardForm.MainPanel.ClientHeight - WizardLangCombo.Height) div 2;
  WizardLangCombo.Anchors := [akTop, akRight];
  AddWizardLang('english', 'English');
  AddWizardLang('german', 'Deutsch');
  AddWizardLang('spanish', 'Español');
  AddWizardLang('french', 'Français');
  AddWizardLang('italian', 'Italiano');
  AddWizardLang('japanese', '日本語');
  AddWizardLang('korean', '한국어');
  AddWizardLang('polish', 'Polski');
  AddWizardLang('brazilianportuguese', 'Português (Brasil)');
  AddWizardLang('thai', 'ไทย');
  AddWizardLang('chinesesimplified', '简体中文');
  AddWizardLang('chinesetraditional', '繁體中文');
  WizardLangCombo.OnChange := @WizardLangChange;

  W := FlagWidth;
  WizardLangFlag := LoadMark('flag_' + FlagCodeOf(ActiveLanguage) + '_' + IntToStr(W) + '.bmp',
    WizardForm.MainPanel, 0, 0);
  WizardLangFlag.Left := WizardLangCombo.Left - WizardLangFlag.Width - ScaleX(6);
  WizardLangFlag.Top := WizardLangCombo.Top + (WizardLangCombo.Height - WizardLangFlag.Height) div 2;
  WizardLangFlag.Anchors := [akTop, akRight];

  // Keep the page title and its line clear of the selector.
  WizardForm.PageNameLabel.Width := WizardLangFlag.Left - WizardForm.PageNameLabel.Left - ScaleX(8);
  WizardForm.PageDescriptionLabel.Width := WizardLangFlag.Left - WizardForm.PageDescriptionLabel.Left - ScaleX(8);
end;

// ---------------------------------------------------------------------------------------------
// The "Starting the toolkit" page
// ---------------------------------------------------------------------------------------------

function NewText(Page: TWizardPage; const Caption: String; Left, Top: Integer; Bold: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(Page);
  Result.Parent := Page.Surface;
  Result.AutoSize := False;
  Result.WordWrap := True;
  Result.Left := Left;
  Result.Top := Top;
  Result.Width := Page.SurfaceWidth - Left;
  Result.Caption := Caption;
  if Bold then Result.Font.Style := [fsBold];
  WizardForm.AdjustLabelHeight(Result);
end;

function NewRadio(Page: TWizardPage; const Caption: String; Top: Integer): TNewRadioButton;
begin
  Result := TNewRadioButton.Create(Page);
  Result.Parent := Page.Surface;
  Result.Left := 0;
  Result.Top := Top;
  Result.Width := Page.SurfaceWidth;
  Result.Caption := Caption;
  Result.Font.Style := [fsBold];
end;

procedure UpdateAutoPage;
begin
  DesktopCheck.Enabled := AutoNoRadio.Checked;
  if (WizardForm <> nil) and (WizardForm.CurPageID = AutoPage.ID) then
    WizardForm.NextButton.Enabled := AutoYesRadio.Checked or AutoNoRadio.Checked or WizardSilent;
end;

procedure AutoClick(Sender: TObject);
begin
  UpdateAutoPage;
end;

procedure CreateAutoPage;
var
  T: TNewStaticText;
  Want, Desk: String;
begin
  AutoPage := CreateCustomPage(LangPage.ID, CustomMessage('AutoPageCaption'), CustomMessage('AutoPageDescription'));
  AutoYesRadio := NewRadio(AutoPage, CustomMessage('AutoYes'), 0);
  T := NewText(AutoPage, CustomMessage('AutoYesHint'), ScaleX(18), AutoYesRadio.Top + AutoYesRadio.Height + ScaleY(2), False);
  AutoNoRadio := NewRadio(AutoPage, CustomMessage('AutoNo'), T.Top + T.Height + ScaleY(14));
  T := NewText(AutoPage, CustomMessage('AutoNoHint'), ScaleX(18), AutoNoRadio.Top + AutoNoRadio.Height + ScaleY(2), False);
  DesktopCheck := TNewCheckBox.Create(AutoPage);
  DesktopCheck.Parent := AutoPage.Surface;
  DesktopCheck.Left := ScaleX(18);
  DesktopCheck.Top := T.Top + T.Height + ScaleY(6);
  DesktopCheck.Width := AutoPage.SurfaceWidth - ScaleX(18);
  DesktopCheck.Caption := CustomMessage('AutoDesktop');
  DesktopCheck.Checked := True;
  DesktopCheck.Visible := False;   // no longer a choice: the shortcut is always made
  AutoYesRadio.OnClick := @AutoClick;
  AutoNoRadio.OnClick := @AutoClick;

  // No default: the player picks. Only a parameter, or an earlier standalone install, preselects.
  Want := Lowercase(ExpandConstant('{param:AUTOSTART|}'));
  if Want = '' then
  begin
    if RegValueExists(HKCU, RunKey, RunValue) then Want := 'yes'
    else if FileExists(StandaloneDefaultDir + '\' + StandaloneExe) then Want := 'no';
  end;
  if Want = 'yes' then AutoYesRadio.Checked := True
  else if Want = 'no' then AutoNoRadio.Checked := True;
  Desk := Lowercase(ExpandConstant('{param:DESKTOP|}'));
  if Desk = 'no' then DesktopCheck.Checked := False
  else if (Desk = '') and (Want = 'no') and FileExists(StandaloneDefaultDir + '\' + StandaloneExe) then
    DesktopCheck.Checked := FileExists(ExpandConstant('{userdesktop}') + '\' + AppName + '.lnk');
  UpdateAutoPage;
end;

// The toolkit's ShareMatchData as config.xml holds it: 'yes', 'no', or '' when it was never saved
// (a fresh install, or a version from before the setting).
function ReadCurrentShare: String;
var
  Xml: AnsiString;
begin
  Result := '';
  if not LoadStringFromFile(StandaloneDataDir + '\config.xml', Xml) then Exit;
  if Pos('<ShareMatchData>true</ShareMatchData>', String(Xml)) > 0 then Result := 'yes'
  else if Pos('<ShareMatchData>false</ShareMatchData>', String(Xml)) > 0 then Result := 'no';
end;

// Match data for the site's community statistics: on unless the player unticks it here (or did
// so before, in the toolkit's settings). /SHAREDATA=yes|no for a silent run.
procedure CreateSharePage;
var
  T: TNewStaticText;
  Want: String;
begin
  SharePage := CreateCustomPage(AutoPage.ID, CustomMessage('SharePageCaption'), CustomMessage('SharePageDescription'));
  ShareCheck := TNewCheckBox.Create(SharePage);
  ShareCheck.Parent := SharePage.Surface;
  ShareCheck.Left := 0;
  ShareCheck.Top := 0;
  ShareCheck.Width := SharePage.SurfaceWidth;
  ShareCheck.Caption := CustomMessage('ShareCheck');
  ShareCheck.Font.Style := [fsBold];
  T := NewText(SharePage, CustomMessage('ShareHint'), ScaleX(18), ShareCheck.Top + ShareCheck.Height + ScaleY(6), False);
  Want := Lowercase(ExpandConstant('{param:SHAREDATA|}'));
  if Want = '' then Want := ReadCurrentShare;
  ShareCheck.Checked := Want <> 'no';
end;

procedure InitializeWizard;
begin
  CreateLanguagePage;
  CreateAutoPage;
  CreateSharePage;
  PreselectLanguage;
  CreateWizardLangSelector;
end;

// The page order: language, start-up, Ready. A restart for another wizard language comes back on the
// page it left (/STARTPAGE=).
function PageRank(const Name: String): Integer;
begin
  if Name = 'auto' then Result := 1
  else if Name = 'share' then Result := 2
  else if Name = 'ready' then Result := 3
  else Result := 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
var
  Start: Integer;
begin
  Result := False;
  Start := PageRank(Lowercase(ExpandConstant('{param:STARTPAGE|}')));
  if (PageID = LangPage.ID) and (Start >= 1) then Result := True;
  if (PageID = AutoPage.ID) and (Start >= 2) and (AutoYesRadio.Checked or AutoNoRadio.Checked) then Result := True;
  if (PageID = SharePage.ID) and (Start >= 3) then Result := True;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  PreselectLanguage;
  Result := CustomMessage('ReadyProgramFolder') + NewLine + Space + ExpandConstant('{app}') + NewLine + NewLine +
    CustomMessage('ReadyLanguage') + NewLine + Space + ChosenLanguageName + NewLine + NewLine +
    CustomMessage('ReadyStart') + NewLine + Space;
  if WantAutostart then Result := Result + CustomMessage('ReadyStartAuto')
  else Result := Result + CustomMessage('ReadyStartManualDesktop');
  Result := Result + NewLine + NewLine + CustomMessage('ReadyShare') + NewLine + Space;
  if ShareCheck.Checked then Result := Result + CustomMessage('ReadyShareYes')
  else Result := Result + CustomMessage('ReadyShareNo');
end;

// ---------------------------------------------------------------------------------------------
// Closing HDT, switching an old plugin install off, installing
// ---------------------------------------------------------------------------------------------

function HdtRunning: Boolean;
var
  Locator, Wmi, Found: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''' + HdtProcessName + '''');
    Result := Found.Count > 0;
  except
    Log('HdtRunning: WMI query failed: ' + GetExceptionMessage);
  end;
end;

function OpenEvent(Access: DWORD; Inherit: BOOL; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(H: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function ResetEvent(H: THandle): BOOL;
  external 'ResetEvent@kernel32.dll stdcall';

function StandaloneRunning: Boolean;
var
  Locator, Wmi, Found: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''' + StandaloneExe + '''');
    Result := Found.Count > 0;
  except
    Log('StandaloneRunning: WMI query failed: ' + GetExceptionMessage);
  end;
end;

// Ask every running copy of the standalone (watcher and toolkit) to close through the event they
// listen on, wait up to 10 s, then end what is left. Needed before its files are replaced.
procedure CloseStandalone;
var
  H: THandle;
  Waited, Code: Integer;
begin
  if not StandaloneRunning then Exit;
  Log('Asking the standalone toolkit to close');
  H := OpenEvent($0002 {EVENT_MODIFY_STATE}, False, QuitEventName);
  if H <> 0 then SetEvent(H);
  Waited := 0;
  while StandaloneRunning and (Waited < 10000) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  if H <> 0 then begin ResetEvent(H); CloseHandle(H); end;
  if StandaloneRunning then
  begin
    Log('The standalone toolkit did not close; ending it');
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + StandaloneExe, '', SW_HIDE, ewWaitUntilTerminated, Code);
  end
  else
    Log('The standalone toolkit is closed');
end;

// Where HDT reads plugins.xml (a portable HDT keeping it next to its exe is not looked for).
function PluginsXmlDir: String;
begin
  Result := HdtDataRoot;
end;

// True when plugins.xml has this toolkit's plugin switched on.
function PluginOnInHdt: Boolean;
var
  Xml: AnsiString;
  Text: String;
  I: Integer;
begin
  Result := False;
  if not LoadStringFromFile(PluginsXmlDir + '\plugins.xml', Xml) then Exit;
  Text := String(Xml);
  I := Pos('HsbgCardLookup/HsbgCardLookup.dll', Text);
  if I = 0 then Exit;
  Text := Copy(Text, I, 400);
  I := Pos('</PluginSettings>', Text);
  if I > 0 then Text := Copy(Text, 1, I);
  Result := Pos('<IsEnabled>true</IsEnabled>', Text) > 0;
end;

// The standalone takes over: set our entry's IsEnabled to false (the files stay, so switching back
// is one click in HDT). HDT is closed at this point.
function DisablePluginInHdt: Boolean;
var
  Doc, Found: Variant;
  F: String;
begin
  Result := False;
  F := PluginsXmlDir + '\plugins.xml';
  if not FileExists(F) then Exit;
  Doc := CreateOleObject('MSXML2.DOMDocument.6.0');
  Doc.async := False;
  Doc.resolveExternals := False;
  Doc.load(F);
  if Doc.parseError.errorCode <> 0 then
    RaiseException('plugins.xml does not parse: ' + Doc.parseError.reason);
  Found := Doc.documentElement.selectNodes('PluginSettings[FileName=''' + PluginFileName + ''']/IsEnabled');
  if (Found.length > 0) and (Found.item(0).text = 'true') then
  begin
    Found.item(0).text := 'false';
    Doc.save(F);
    Result := True;
  end;
end;

// Ask HDT to close the way its own Exit does: WM_CLOSE on the main window runs Core.Shutdown,
// which saves config and plugins.xml and unloads the plugins. With HDT's "close to tray" option
// on, that only hides the window — then the user has to exit it from the tray icon (the message
// below says so). Never a kill: a killed HDT does not write plugins.xml.
procedure AskHdtToClose;
var
  H: HWND;
begin
  H := FindWindowByWindowName(HdtWindowTitle);
  if H <> 0 then
  begin
    Log('Asking Hearthstone Deck Tracker to close');
    PostMessage(H, WM_CLOSE, 0, 0);
  end
  else
    Log('Hearthstone Deck Tracker main window not found; waiting for the user to close it');
end;

function WaitForHdtClosed(TimeoutMs: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while HdtRunning and (Waited < TimeoutMs) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  Result := not HdtRunning;
end;

// True once HDT is not running; False when the user gives up. The first wait covers HDT's own
// shutdown (its PrepareShutdown polls up to ~5 s); each Retry asks again and waits a little.
function EnsureHdtClosed: Boolean;
var
  TimeoutMs: Integer;
begin
  Result := True;
  if not HdtRunning then Exit;
  AskHdtToClose;
  TimeoutMs := 20000;
  while not WaitForHdtClosed(TimeoutMs) do
  begin
    Log('Hearthstone Deck Tracker is still running; asking the user');
    if SuppressibleMsgBox(CustomMessage('HdtStillRunning'), mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
    AskHdtToClose;
    TimeoutMs := 5000;
  end;
  Log('Hearthstone Deck Tracker is closed');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // A running standalone holds its files (an upgrade): close it.
  CloseStandalone;
  if StandaloneRunning then
  begin
    Result := CustomMessage('ToolkitRunningAbort');
    Exit;
  end;
  // To switch an old plugin install off, HDT must be closed, or its shutdown writes plugins.xml back.
  if PluginOnInHdt and not EnsureHdtClosed then
    Result := CustomMessage('HdtRunningAbort');
end;

function InitializeUninstall: Boolean;
begin
  if FileExists(ExpandConstant('{app}\' + StandaloneExe)) then
  begin
    CloseStandalone;
    Result := not StandaloneRunning;
  end
  else
    Result := EnsureHdtClosed;
end;

// The standalone's autostart entry is ours to remove with it.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and FileExists(ExpandConstant('{app}\' + StandaloneExe)) then
    RegDeleteValue(HKCU, RunKey, RunValue);
end;

procedure RemoveAutostart;
begin
  if RegValueExists(HKCU, RunKey, RunValue) then
  begin
    RegDeleteValue(HKCU, RunKey, RunValue);
    Log('Removed the standalone toolkit''s autostart');
  end;
end;

// The plugin applies setup-language.txt once at its next start and deletes it. Written only when
// the choice differs from what config.xml already holds (always on a fresh install), so an upgrade
// that keeps the setting leaves nothing behind.
procedure HandOverLanguage;
var
  Code: String;
begin
  PreselectLanguage;
  Code := ChosenLanguage;
  CurrentLang := ReadCurrentLanguage;
  if (CurrentLang <> '?') and (CompareText(Code, CurrentLang) = 0) then
  begin
    Log('Toolkit language unchanged (' + Code + ')');
    Exit;
  end;
  ForceDirectories(DataDir(''));
  if SaveStringToFile(DataDir('') + '\setup-language.txt', Code, False) then
    Log('Toolkit language handed to the plugin: "' + Code + '"')
  else
    Log('Could not write setup-language.txt');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  // A standalone that never ran here (no config.xml of its own; a former plugin user counts too):
  // the toolkit opens its settings once at its next start, then deletes the marker.
  if not FileExists(StandaloneDataDir + '\config.xml') then
  begin
    ForceDirectories(StandaloneDataDir);
    if SaveStringToFile(StandaloneDataDir + '\setup-welcome.txt', '1', False) then
      Log('Fresh standalone install: the toolkit will open its settings once');
  end;
  HandOverLanguage;
  // The toolkit applies setup-share.txt once at its next start and deletes it.
  ForceDirectories(StandaloneDataDir);
  if ShareCheck.Checked then SaveStringToFile(StandaloneDataDir + '\setup-share.txt', '1', False)
  else SaveStringToFile(StandaloneDataDir + '\setup-share.txt', '0', False);
  Log('Match data sharing handed to the toolkit: ' + IntToStr(Ord(ShareCheck.Checked)));
  if WantAutostart then
  begin
    RegWriteStringValue(HKCU, RunKey, RunValue, '"' + ExpandConstant('{app}\' + StandaloneExe) + '" --autostart');
    Log('The standalone toolkit starts with Windows');
  end
  else
    RemoveAutostart;
  try
    PluginDisabled := DisablePluginInHdt;
    if PluginDisabled then Log('Switched the HDT plugin off in ' + PluginsXmlDir + '\plugins.xml');
  except
    Log('Could not switch the HDT plugin off: ' + GetExceptionMessage);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Note, Caption, ClickFinish: String;
  Delta: Integer;
begin
  if WizardLangCombo <> nil then
  begin
    WizardLangCombo.Visible := (CurPageID = LangPage.ID) or (CurPageID = AutoPage.ID) or (CurPageID = SharePage.ID) or (CurPageID = wpReady);
    WizardLangFlag.Visible := WizardLangCombo.Visible;
  end;
  if CurPageID = LangPage.ID then
  begin
    PreselectLanguage;
    Exit;
  end;
  if CurPageID = AutoPage.ID then
  begin
    UpdateAutoPage;
    Exit;
  end;
  if CurPageID = wpReady then
  begin
    WizardForm.ReadyLabel.Caption := CustomMessage('ReadyStandalone');
    Exit;
  end;
  if CurPageID <> wpFinished then Exit;
  WizardForm.FinishedLabel.Caption := FmtMessage(CustomMessage('FinishedStandalone'), [AppName]) + #13#10#13#10 + SetupMessage(msgClickFinish);
  if WantAutostart then Note := CustomMessage('FinishedAuto') else Note := CustomMessage('FinishedManual');
  if PluginDisabled then Note := Note + #13#10#13#10 + CustomMessage('PluginSwitchedOff');
  // Inno has already laid the page out for its own text (ending in "Click Finish to exit Setup.")
  // when this runs: put the note before that line, then grow the label and move the
  // "Start the toolkit" box down by as much, or the note is cut off behind it.
  Caption := WizardForm.FinishedLabel.Caption;
  ClickFinish := SetupMessage(msgClickFinish);
  if Pos(ClickFinish, Caption) > 0 then
    StringChangeEx(Caption, ClickFinish, Note + #13#10#13#10 + ClickFinish, True)
  else
    Caption := Caption + #13#10#13#10 + Note;
  WizardForm.FinishedLabel.Caption := Caption;
  Delta := WizardForm.AdjustLabelHeight(WizardForm.FinishedLabel);
  WizardForm.RunList.Top := WizardForm.RunList.Top + Delta;
end;
