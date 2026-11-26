# Monitor Control

`MonitorControl.exe`를 실행한 뒤 창 상단에서 제어할 모니터를 선택합니다.

### 삼성 ViewFinity S9에서 Wi-Fi로 연결

1. PC와 모니터를 같은 로컬 네트워크에 연결합니다. 모니터에서 **설정 → 전체 설정 → 연결 → 네트워크 → 전문가 설정 → IP 리모컨**을 켭니다.
2. 모니터의 **네트워크 상태 → IP 설정**에서 IPv4 주소를 확인하고 PC 입력 화면으로 돌아갑니다.
3. 앱에서 **삼성 모니터 (Wi-Fi)**를 선택하고 모니터 IP를 입력합니다. **페어링**을 누른 뒤 모니터 화면에서 연결을 허용합니다.
4. **밝기 낮추기 / 높이기**를 누르면 백라이트가 5단계씩(0~50), **볼륨 낮추기 / 높이기**를 누르면 볼륨이 10단계씩(0~100) 바뀝니다. 앱에 표시되는 값은 변경 후 모니터에서 다시 읽은 값입니다.

Wi-Fi 모드의 밝기·볼륨 슬라이더에서 원하는 눈금을 클릭하면 손잡이가 해당 값으로 바로 이동합니다. 클릭한 채로 드래그해 0%부터 100%까지 10% 단위로 다시 선택할 수 있습니다. 마우스나 방향키를 놓고 0.6초가 지나면 변경한 항목별로 마지막 값만 전송합니다. 휠로 조절할 때도 마지막 입력 후 같은 시간이 적용됩니다. 연결할 때와 변경 후에는 모니터의 실제 값을 표시하므로 54%처럼 눈금 사이의 값도 확인할 수 있습니다.

앱은 삼성 IP Control(HTTPS, TCP 1516)을 사용합니다. 페어링 정보는 현재 Windows 사용자 계정으로 암호화해 `%LOCALAPPDATA%\AsusMonitorKeys\samsung.dat`에 저장합니다. 다음 실행 시 페어링된 모니터가 자동으로 선택됩니다. 주소가 바뀌었거나 모니터가 인증을 거부하면 주소를 확인하고 다시 페어링하세요. [삼성 IP 리모컨 설정 안내](https://image-us.samsung.com/SamsungUS/samsungbusiness/tv-ci-resources/Samsung-IP-Control.pdf)

### DDC/CI 모니터

**현재 창의 모니터 (DDC/CI)**를 선택하고 조절할 모니터로 앱 창을 옮긴 뒤 밝기 또는 볼륨 버튼을 누릅니다.

### 빌드

64비트 Windows에서 소스 파일과 `MonitorControl.ico`가 있는 폴더를 PowerShell로 열고 .NET Framework 4.8 C# 컴파일러를 실행합니다.

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /r:System.Security.dll /r:System.Web.Extensions.dll /win32icon:MonitorControl.ico /resource:MonitorControl.ico,MonitorControl.ico /out:MonitorControl.exe MonitorControl.cs SamsungMonitor.cs LevelSlider.cs
```

빌드가 완료되면 같은 폴더에 `MonitorControl.exe`가 생성됩니다.
