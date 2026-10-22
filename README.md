# UNCLAIMED / 미수령

야간 택배 배달을 소재로 제작 중인 1인칭 심리 공포게임.

## 개발 환경

- Unity 6000.6.6f1
- Universal Render Pipeline
- Windows PC
- Blender로 제작하는 3D 에셋

## 현재 저장된 작업

- 1인칭 이동과 마우스 시점
- E키 상호작용과 화면 안내
- 아파트 외부 공간과 택배차 운전석의 기본 배치
- 운전석 시점 기준점 `DriverView`

작업 씬: `Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity`

차량에서 시작하고 하차하는 기능은 아직 적용하지 않았다.

## 프로젝트 열기

1. Git LFS를 설치한 환경에서 이 저장소를 복제한다.
2. Unity Hub에서 프로젝트 폴더를 추가한다.
3. Unity 6000.6.6f1로 열고 패키지와 에셋 가져오기가 끝날 때까지 기다린다.
4. 작업 씬을 연다.

`Assets`, `.meta` 파일, `Packages`, `ProjectSettings`를 함께 관리한다.
`Library`, `Temp`, `Logs`와 개인 설정은 Unity에서 다시 생성하므로 업로드하지 않는다.
Blender 원본과 FBX 등 큰 바이너리 에셋은 Git LFS로 관리한다.
