# MainWindow 시각 트리 설명

## 02-a: 최상위 레이아웃

`MainWindow.xaml`은 먼저 `Window` 안에 루트 `Grid`를 두고, 이 `Grid`가 화면을 두 행으로 나눕니다.

- `Grid.Row=0`: 상단 Header입니다. 로고, 시스템명, 현재 시각을 표시합니다.
- `Grid.Row=1`: 본문 Body입니다. 다시 두 열로 나뉩니다.
- `Grid.Column=0`: 왼쪽 Sidebar입니다. PLC 상태, 운전 제어, 생산 수량, 비전 요약처럼 항상 보여야 하는 정보를 둡니다.
- `Grid.Column=1`: 오른쪽 Content입니다. `TabControl`이 들어가는 실제 작업 영역입니다.

핵심은 `Window → Grid → Header / Body → Sidebar / Content` 순서로 공간이 나뉜다는 점입니다.

## 02-b: Sidebar와 TabControl 내부

Body 안쪽은 두 가지 역할로 나뉩니다.

- Sidebar는 `Border → ScrollViewer → StackPanel` 구조입니다.
  화면 높이가 부족하면 스크롤되고, 내부에는 PLC 상태 카드, 운전 모드, 제어 버튼, 생산/비전 요약 패널이 세로로 쌓입니다.

- Content는 `TabControl`입니다.
  각 `TabItem`은 탭 제목과 실제 화면 UserControl을 연결합니다.

탭과 View 매핑은 다음과 같습니다.

| 탭 | UserControl |
|---|---|
| 모니터링 | `MonitorView` |
| 비전검사 | `VisionInspectionView` |
| 수동 조작 | `ManualControlView` |
| 품질 분석 | `QualityAnalysisView` |
| 생산현황 | `ProductionStatusView` |
| DB 조회 | `DbSearchView` |
| 생산 이력 | `ProductionHistoryView` |
| 이벤트 로그 | `EventLogView` |

즉 `MainWindow`는 모든 세부 화면을 직접 구현하기보다, 공통 외곽 레이아웃을 제공하고 각 업무 화면은 별도 `UserControl`로 분리해 탭에 꽂는 구조입니다.
