; 冷库宝安装脚本 - RSA 签名授权版

#define MyAppName "冷库宝"
#define MyAppVersion "1.0.0.1"
#define MyAppPublisher "天马果业"
#define MyAppExeName "lengkubao.desktop.exe"

[Setup]
AppId={{9E8B24FC-F103-4DAC-8B45-15ECD430E5E3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
OutputBaseFilename=冷库宝_Setup_v1.0.0.1
OutputDir=C:\Users\35292\Desktop\Output
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\afilessss\lengkubao.desktop\lengkubao.desktop\lengkubao.desktop\bin\Release\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\afilessss\lengkubao.desktop\lengkubao.desktop\lengkubao.desktop\bin\Release\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\afilessss\lengkubao.desktop\lengkubao.desktop\lengkubao.desktop\bin\Release\*.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\afilessss\lengkubao.desktop\lengkubao.desktop\lengkubao.desktop\bin\Release\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs
Source: "D:\afilessss\lengkubao.desktop\lengkubao.desktop\lengkubao.desktop\bin\Release\x86\*"; DestDir: "{app}\x86"; Flags: ignoreversion recursesubdirs
Source: "Lengkubao.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\afilessss\lengkubao.desktop\tools\LicenseVerify\bin\Release\LicenseVerify.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Lengkubao.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{commonappdata}\Lengkubao\license.lic"
Type: dirifempty; Name: "{commonappdata}\Lengkubao"

[Code]
var
  AuthPage: TInputQueryWizardPage;
  MachineID: string;
  LicenseCode: string;
  LicenseType: string;
  TrialPlan: string;
  ExpiresAt: string;
  SignedPayload: string;
  SkipAuthBecauseLicensed: Boolean;

function LicenseAlreadyExists: Boolean;
var
  LicensePath: string;
begin
  LicensePath := ExpandConstant('{commonappdata}\Lengkubao\license.lic');
  Result := FileExists(LicensePath);
end;

function GetComputerNameFromEnv: string;
begin
  Result := GetEnv('COMPUTERNAME');
  if Result = '' then
    Result := 'PC' + IntToStr(Random(10000));
end;

function GetUserNameFromEnv: string;
begin
  Result := GetEnv('USERNAME');
  if Result = '' then
    Result := 'USER';
end;

function GetProductID: string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'ProductId', Result) then
    Result := '00000';
end;

function GetComputerGUID: string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Cryptography', 'MachineGuid', Result) then
    Result := '00000000';
end;

function SimpleHash(Input: string): LongWord;
var
  i: Integer;
begin
  Result := 0;
  for i := 1 to Length(Input) do
  begin
    Result := Result + Ord(Input[i]);
    Result := Result * 7;
  end;
  Result := Result mod 100000000;
end;

function GetMachineID: string;
var
  ComputerName: string;
  UserName: string;
  ProductID: string;
  MachineGUID: string;
  HashValue: LongWord;
begin
  ComputerName := GetComputerNameFromEnv;
  UserName := GetUserNameFromEnv;
  ProductID := GetProductID;
  MachineGUID := GetComputerGUID;
  HashValue := SimpleHash(ComputerName + UserName + ProductID + MachineGUID);
  if Length(ComputerName) >= 4 then
    Result := Copy(ComputerName, 1, 4) + '-' + IntToStr(HashValue)
  else
    Result := ComputerName + '-' + IntToStr(HashValue);
  Result := Uppercase(Result);
end;

function VerifyLicenseSignature(MachineId, Signature: string): Boolean;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile('LicenseVerify.exe');
  if Exec(ExpandConstant('{tmp}\LicenseVerify.exe'),
    'verify "' + MachineId + '" "' + Signature + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
end;

function VerifyLicenseAndGetInfo(MachineId, Signature: string; var LicenseType, TrialPlan, ExpiresAt, SignedPayload: string): Boolean;
var
  ResultCode: Integer;
  InfoFile: string;
  Lines: TArrayOfString;
  InfoText: string;
  i: Integer;
begin
  Result := False;
  LicenseType := '';
  TrialPlan := '';
  ExpiresAt := '';
  SignedPayload := MachineId;

  ExtractTemporaryFile('LicenseVerify.exe');
  InfoFile := ExpandConstant('{tmp}\license_info.txt');
  if FileExists(InfoFile) then
    DeleteFile(InfoFile);

  if not Exec(ExpandConstant('{tmp}\LicenseVerify.exe'),
    'verifyinfo "' + MachineId + '" "' + Signature + '" "' + InfoFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;

  if (ResultCode <> 0) or (not FileExists(InfoFile)) then
    Exit;

  if not LoadStringsFromFile(InfoFile, Lines) then
    Exit;

  InfoText := '';
  for i := 0 to GetArrayLength(Lines) - 1 do
    InfoText := InfoText + Lines[i];

  if InfoText = 'PERM' then
  begin
    LicenseType := 'Permanent';
    SignedPayload := MachineId;
    Result := True;
    Exit;
  end;

  if Copy(InfoText, 1, 6) = 'TRIAL|' then
  begin
    LicenseType := 'Trial';
    Delete(InfoText, 1, 6);
    i := Pos('|', InfoText);
    if i > 0 then
    begin
      TrialPlan := Copy(InfoText, 1, i - 1);
      ExpiresAt := Copy(InfoText, i + 1, MaxInt);
    end
    else
      TrialPlan := InfoText;
    SignedPayload := MachineId + '|TRIAL|' + TrialPlan;
    Result := True;
  end;
end;

procedure SaveLicenseFile(MachineId, Signature, LicenseType, TrialPlan, ExpiresAt, SignedPayload: string);
var
  LicensePath: string;
  Lines: TArrayOfString;
begin
  LicensePath := ExpandConstant('{commonappdata}\Lengkubao\license.lic');
  ForceDirectories(ExtractFilePath(LicensePath));
  SetArrayLength(Lines, 6);
  Lines[0] := 'MachineId=' + MachineId;
  Lines[1] := 'Signature=' + Signature;
  Lines[2] := 'LicenseType=' + LicenseType;
  Lines[3] := 'TrialPlan=' + TrialPlan;
  Lines[4] := 'ExpiresAt=' + ExpiresAt;
  Lines[5] := 'SignedPayload=' + SignedPayload;
  SaveStringsToFile(LicensePath, Lines, False);
end;

procedure DeleteOldShortcuts;
begin
  if FileExists(ExpandConstant('{autodesktop}\{#MyAppName}.lnk')) then
    DeleteFile(ExpandConstant('{autodesktop}\{#MyAppName}.lnk'));
  if FileExists(ExpandConstant('{autoprograms}\{#MyAppName}.lnk')) then
    DeleteFile(ExpandConstant('{autoprograms}\{#MyAppName}.lnk'));
end;

procedure InitializeWizard;
begin
  MachineID := GetMachineID;
  LicenseCode := '';
  LicenseType := '';
  TrialPlan := '';
  ExpiresAt := '';
  SignedPayload := '';
  SkipAuthBecauseLicensed := LicenseAlreadyExists;

  AuthPage := CreateInputQueryPage(wpWelcome,
    '冷库宝 - 安装验证',
    '请获取并输入 RSA 安装授权码',
    '═══════════════════════════════════' + #13#10 +
    '        机 器 码：' + MachineID + #13#10 +
    '═══════════════════════════════════' + #13#10 + #13#10 +
    '【获取授权码】' + #13#10 +
    '1. 将上方机器码发给管理员获取授权码（一年、三年或永久）' + #13#10 +
    '2. 粘贴完整授权码（含连字符）后点击下一步' + #13#10 + #13#10 +
    '升级安装时已激活用户将自动跳过此步骤。' + #13#10 + #13#10 +
    '授权码：');

  AuthPage.Add('', False);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if (PageID = AuthPage.ID) and SkipAuthBecauseLicensed then
    Result := True
  else
    Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  EnteredCode: string;
  LicType: string;
  TPlan: string;
  ExpAt: string;
  SPayload: string;
begin
  Result := True;

  if CurPageID = AuthPage.ID then
  begin
    EnteredCode := Trim(AuthPage.Values[0]);
    if EnteredCode = '' then
    begin
      MsgBox('请输入授权码。', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if not VerifyLicenseAndGetInfo(MachineID, EnteredCode, LicType, TPlan, ExpAt, SPayload) then
    begin
      MsgBox('授权码错误！' + #13#10 + #13#10 +
             '机器码：' + MachineID + #13#10 + #13#10 +
             '请联系管理员获取正确的授权码。',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;

    LicenseCode := EnteredCode;
    LicenseType := LicType;
    TrialPlan := TPlan;
    ExpiresAt := ExpAt;
    SignedPayload := SPayload;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    DeleteOldShortcuts;
    if LicenseCode <> '' then
      SaveLicenseFile(MachineID, LicenseCode, LicenseType, TrialPlan, ExpiresAt, SignedPayload);
  end;

  if CurStep = ssDone then
  begin
    if not SkipAuthBecauseLicensed then
      MsgBox('冷库宝安装完成！' + #13#10 + #13#10 +
             '感谢使用天马果业冷库宝系统。',
             mbInformation, MB_OK);
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := MsgBox('确定要卸载冷库宝吗？' + #13#10 + #13#10 +
                   '卸载后将清除本机授权信息，重新安装需要再次输入授权码。',
                   mbConfirmation, MB_YESNO) = IDYES;
end;
