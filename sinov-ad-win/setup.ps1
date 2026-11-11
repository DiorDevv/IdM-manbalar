# Bank IdM sinov AD (Windows Server 2025): domen BANK.TEST, bank darajasidagi qattiq sozlamalar.
# Bosqichlar (C:\idm-setup\stage): 0 — IP, sertifikat, AD DS rolini o'rnatish va domen ko'tarish (qayta yuklanadi);
# 1 — OU, xizmat akkauntlari va delegatsiya, xodimlar, guruhlar, LDAP signing + channel binding (qayta yuklanadi); 2 — tayyor.
# Har bosqich holati C:\idm-setup\status.txt da. Parollar config.json da (faqat VM ichida, sozlama diski olib tashlanadi).
$ErrorActionPreference = 'Stop'
$Root = 'C:\idm-setup'
$StageFile = Join-Path $Root 'stage'
$Log = Join-Path $Root 'status.txt'
function Say($m) { Add-Content -Path $Log -Value ("{0:u} {1}" -f (Get-Date), $m) }
$cfg = Get-Content (Join-Path $Root 'config.json') -Raw | ConvertFrom-Json
$stage = if (Test-Path $StageFile) { [int](Get-Content $StageFile) } else { 0 }
$Base = 'DC=bank,DC=test'

try {
  switch ($stage) {
    0 {
      Say 'Bosqich 0: tarmoq, sertifikat, AD DS'
      # QEMU user tarmog'i: 10.0.2.0/24, shlyuz 10.0.2.2. DC'da statik IP va DNS — o'zi
      $nic = Get-NetAdapter | Where-Object Status -eq 'Up' | Select-Object -First 1
      if (-not (Get-NetIPAddress -InterfaceIndex $nic.ifIndex -IPAddress 10.0.2.15 -ErrorAction SilentlyContinue | Where-Object PrefixOrigin -eq 'Manual')) {
        Set-NetIPInterface -InterfaceIndex $nic.ifIndex -Dhcp Disabled
        Get-NetIPAddress -InterfaceIndex $nic.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
        New-NetIPAddress -InterfaceIndex $nic.ifIndex -IPAddress 10.0.2.15 -PrefixLength 24 -DefaultGateway 10.0.2.2 | Out-Null
      }
      Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ServerAddresses 127.0.0.1
      # LDAPS sertifikati: sinov CA'si (host'da yaratilgan), SAN: dc1.bank.test, localhost, 127.0.0.1
      Import-Certificate -FilePath (Join-Path $Root 'ca.crt') -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
      $pfxPass = ConvertTo-SecureString $cfg.PfxPassword -AsPlainText -Force
      Import-PfxCertificate -FilePath (Join-Path $Root 'dc1.pfx') -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPass | Out-Null
      Install-WindowsFeature AD-Domain-Services -IncludeManagementTools | Out-Null
      # Keyingi bosqichlar: har yuklanishda SYSTEM nomidan (domen ko'tarilgach avto-kirish ishlamaydi)
      $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File $Root\setup.ps1"
      Register-ScheduledTask -TaskName 'IdmSetup' -Action $action -Trigger (New-ScheduledTaskTrigger -AtStartup) -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
      Set-Content $StageFile 1
      Say 'Domen ko''tarilmoqda (bank.test), keyin qayta yuklanadi'
      $dsrm = ConvertTo-SecureString $cfg.DsrmPassword -AsPlainText -Force
      Install-ADDSForest -DomainName 'bank.test' -DomainNetbiosName 'BANK' -SafeModeAdministratorPassword $dsrm -InstallDns -Force | Out-Null
    }
    1 {
      Say 'Bosqich 1: AD xizmatlarini kutish'
      for ($i = 0; $i -lt 60; $i++) { try { Get-ADDomain | Out-Null; break } catch { Start-Sleep 10 } }
      Import-Module ActiveDirectory
      Set-DnsServerForwarder -IPAddress 10.0.2.3 -ErrorAction SilentlyContinue

      function Ensure-OU($name, $path) {
        if (-not (Get-ADOrganizationalUnit -Filter "Name -eq '$name'" -SearchBase $path -SearchScope OneLevel -ErrorAction SilentlyContinue)) {
          New-ADOrganizationalUnit -Name $name -Path $path -ProtectedFromAccidentalDeletion $true
        }
      }
      Ensure-OU 'Xodimlar' $Base
      Ensure-OU 'Guruhlar' $Base
      Ensure-OU 'Xizmat' $Base
      # Ishdan ketganlar Xodimlar ichida: IdM yozish qamrovi (UsersDn) shu OU bilan cheklangan
      Ensure-OU 'Ishdan ketganlar' "OU=Xodimlar,$Base"
      foreach ($b in $cfg.Branches) { Ensure-OU $b "OU=Xodimlar,$Base" }

      # Xizmat akkauntlari: o'qish (oddiy domen foydalanuvchisi) va yozish (faqat delegatsiya qilingan OU'larda)
      function Ensure-Service($sam, $password, $desc) {
        if (-not (Get-ADUser -Filter "SamAccountName -eq '$sam'" -ErrorAction SilentlyContinue)) {
          New-ADUser -Name $sam -SamAccountName $sam -UserPrincipalName "$sam@bank.test" -Path "OU=Xizmat,$Base" -Description $desc `
            -AccountPassword (ConvertTo-SecureString $password -AsPlainText -Force) -Enabled $true -PasswordNeverExpires $true -CannotChangePassword $true
        }
      }
      Ensure-Service 'svc-idm' $cfg.SvcPassword 'IdM sinxronlash (faqat o''qish)'
      Ensure-Service 'svc-idm-write' $cfg.WritePassword 'IdM tizimga yozish (delegatsiya: Xodimlar, Ishdan ketganlar, Guruhlar)'

      # Eng kam huquq (Xodimlar va uning ichidagi OU'lar): foydalanuvchi yaratish/o'chirish (OU orasida ko'chirish uchun ham), atributlar,
      # parolni tiklash; guruhda faqat member
      $ou = "OU=Xodimlar,$Base"
      dsacls $ou /I:T /G 'BANK\svc-idm-write:CCDC;user' | Out-Null
      dsacls $ou /I:S /G 'BANK\svc-idm-write:WP;;user' | Out-Null
      dsacls $ou /I:S /G 'BANK\svc-idm-write:CA;Reset Password;user' | Out-Null
      dsacls "OU=Guruhlar,$Base" /I:S /G 'BANK\svc-idm-write:WP;member;group' | Out-Null

      # Xodimlar (HR sinov fayli bilan bir xil tabel raqamlari), paroli sinov uchun ma'lum: Parol123!
      $people = @{}
      foreach ($p in $cfg.People) {
        $ou = "OU=$($p.Branch),OU=Xodimlar,$Base"
        $u = Get-ADUser -Filter "SamAccountName -eq '$($p.Login)'" -ErrorAction SilentlyContinue
        if (-not $u) {
          New-ADUser -Name "$($p.LastName) $($p.FirstName)" -GivenName $p.FirstName -Surname $p.LastName -DisplayName "$($p.LastName) $($p.FirstName)" `
            -SamAccountName $p.Login -UserPrincipalName "$($p.Login)@bank.test" -EmailAddress "$($p.Login)@bank.test" -Path $ou `
            -Department $p.Department -Title $p.Title -Office $p.Branch -EmployeeNumber $p.EmployeeNumber `
            -AccountPassword (ConvertTo-SecureString 'Parol123!' -AsPlainText -Force) -Enabled $true -PasswordNeverExpires $true
          $u = Get-ADUser -Filter "SamAccountName -eq '$($p.Login)'"
        }
        $people[$p.EmployeeNumber] = $u
      }
      foreach ($p in $cfg.People | Where-Object Manager) {
        if ($people[$p.Manager]) { Set-ADUser $people[$p.EmployeeNumber] -Manager $people[$p.Manager] }
      }
      foreach ($g in $cfg.Groups) {
        if (-not (Get-ADGroup -Filter "SamAccountName -eq '$($g.Name)'" -ErrorAction SilentlyContinue)) {
          New-ADGroup -Name $g.Name -SamAccountName $g.Name -GroupScope Global -GroupCategory Security -Path "OU=Guruhlar,$Base"
        }
        foreach ($m in $g.Members) { Add-ADGroupMember -Identity $g.Name -Members $m -ErrorAction SilentlyContinue }
      }

      # Bank darajasi: LDAP imzolash va channel binding majburiy (oddiy LDAP'da parol bilan kirish rad etiladi)
      Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\NTDS\Parameters' -Name 'LDAPServerIntegrity' -Value 2 -Type DWord
      Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\NTDS\Parameters' -Name 'LdapEnforceChannelBinding' -Value 2 -Type DWord
      Set-Content $StageFile 2
      Say 'Bosqich 1 tugadi: qayta yuklanadi'
      Restart-Computer -Force
    }
    2 {
      Say 'Tayyor: ldaps://dc1.bank.test:636'
      Unregister-ScheduledTask -TaskName 'IdmSetup' -Confirm:$false -ErrorAction SilentlyContinue
      # Sozlama papkasidagi parollar endi kerak emas
      Remove-Item (Join-Path $Root 'config.json'), (Join-Path $Root 'dc1.pfx') -Force -ErrorAction SilentlyContinue
      Set-Content $StageFile 3
    }
  }
} catch {
  Say ("XATO (bosqich {0}): {1}" -f $stage, $_.Exception.Message)
  throw
}
