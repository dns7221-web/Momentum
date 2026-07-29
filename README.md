# Momentum

물리 기반 웹 스윙(web-swinging) 이동 시스템 포트폴리오.

## 환경
- Unity **6000.3.14f1**
- Universal Render Pipeline (URP)
- New Input System

## 목표
로프/웹의 장력, 진자 운동, 운동량 보존을 실제 물리로 계산해
"감으로 튜닝한 애니메이션"이 아닌 시뮬레이션 기반 이동을 구현한다.

### 구현 예정
- [ ] 웹 발사 및 부착 지점 판정 (raycast → 건물 표면)
- [ ] 진자 스윙 물리 (장력 / 원심력 / 중력)
- [ ] 로프 길이 조절 (감기·풀기)에 따른 각운동량 변화
- [ ] 릴리즈 시 운동량 보존 및 탄도 궤적
- [ ] 벽 러닝 / 착지 전환
- [ ] 도시 레벨 구성

## 구조
```
Assets/
  Scenes/      씬
  Settings/    URP 렌더 파이프라인 설정
```

## 참고
3D 에셋은 Git LFS로 관리한다. 클론 후:
```
git lfs install
git lfs pull
```
