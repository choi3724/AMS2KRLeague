using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AMS2LeagueClient.Presentation
{
    public sealed class ClientStatusViewModel : INotifyPropertyChanged
    {
        private string _stateLabel = "대기 중";
        private string _message = "Automobilista 2를 기다리는 중입니다...";
        private string _detail = "오버레이 숨김 · 1초마다 프로세스 확인";
        private string _accentColor = "#F7B84B";
        private string _processText = "AMS2 프로세스: 감지되지 않음";
        private string _sharedMemoryText = "공유 메모리: 연결되지 않음";
        private string _windowText = "게임 창: 감지되지 않음";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string StateLabel { get => _stateLabel; set => Set(ref _stateLabel, value); }
        public string Message { get => _message; set => Set(ref _message, value); }
        public string Detail { get => _detail; set => Set(ref _detail, value); }
        public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
        public string ProcessText { get => _processText; set => Set(ref _processText, value); }
        public string SharedMemoryText { get => _sharedMemoryText; set => Set(ref _sharedMemoryText, value); }
        public string WindowText { get => _windowText; set => Set(ref _windowText, value); }

        public void SetWaiting()
        {
            StateLabel = "대기 중";
            Message = "Automobilista 2를 기다리는 중입니다...";
            Detail = "오버레이 숨김 · 1초마다 재연결 확인 · 바쁜 대기 없음";
            AccentColor = "#F7B84B";
            ProcessText = "AMS2 프로세스: 감지되지 않음";
            SharedMemoryText = "공유 메모리: 연결되지 않음";
            WindowText = "게임 창: 감지되지 않음";
        }

        public void SetSharedMemoryUnavailable(int pid)
        {
            StateLabel = "설정 확인 필요";
            Message = "AMS2 공유 메모리를 사용할 수 없습니다.";
            Detail = "옵션 → 시스템 → 공유 메모리 → Project CARS 2를 선택하세요. 클라이언트는 이 설정을 변경하지 않습니다.";
            AccentColor = "#FF6B6B";
            ProcessText = "AMS2 프로세스: 연결됨 (PID " + pid + ")";
            SharedMemoryText = "공유 메모리: $pcars2$ 사용 불가";
        }

        public void SetAttached(int pid, uint version, uint build, string windowDetails)
        {
            StateLabel = "연결됨";
            Message = "읽기 전용 텔레메트리에 연결되었습니다.";
            Detail = "AMS2가 전면에 있고 플레이 상태가 유효할 때만 레이스 HUD가 표시됩니다.";
            AccentColor = "#4DE3B1";
            ProcessText = "AMS2 프로세스: 연결됨 (PID " + pid + ")";
            SharedMemoryText = "공유 메모리: v" + version + " · 빌드 " + build;
            WindowText = windowDetails;
        }

        public void SetDemo(bool diagnostic)
        {
            StateLabel = "데모 / 시뮬레이션";
            Message = "고정 데이터 오버레이를 실행 중입니다.";
            Detail = "이 모드는 UI와 창 동작만 검증하며 실제 AMS2 텔레메트리 증거가 아닙니다.";
            AccentColor = "#B68CFF";
            ProcessText = "AMS2 프로세스: --demo로 생략";
            SharedMemoryText = "공유 메모리: 고정 데이터 v14";
            WindowText = diagnostic ? "오버레이: 진단용 고정 데이터" : "오버레이: 기본 고정 데이터";
        }

        private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
