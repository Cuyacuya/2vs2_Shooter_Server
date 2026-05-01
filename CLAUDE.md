# CLAUDE.md

## 1. Think Before Coding
"Don't assume. Don't hide confusion. Surface tradeoffs."

구현 전에 가정을 명시하고, 불확실한 부분은 질문하며, 여러 해석이 있으면 제시해야 합니다. 더 간단한 방법이 있으면 언급하고, 혼란스러운 부분이 있으면 멈춰서 이름 붙이고 물어봅시다.

## 2. Simplicity First
"Minimum code that solves the problem. Nothing speculative."

요청한 것 이상의 기능은 추가하지 않고, 단일 사용 코드의 추상화, 미요청 유연성/설정 가능성, 불가능한 시나리오의 오류 처리를 피합니다. 200줄이 50줄로 줄일 수 있다면 다시 작성하세요.

## 3. Surgical Changes
"Touch only what you must. Clean up only your own mess."

기존 코드 편집 시 인접한 코드/주석/형식을 개선하지 말고, 깨지지 않은 것을 리팩터하지 마세요. 기존 스타일을 따르고, 관련 없는 미사용 코드는 언급만 합니다. 변경으로 인한 미사용 요소는 제거합니다.

## 4. Goal-Driven Execution
"Define success criteria. Loop until verified."

작업을 검증 가능한 목표로 변환하고, 다단계 작업에는 간단한 계획을 제시하세요. 강력한 성공 기준은 독립적 반복을 가능하게 합니다.
