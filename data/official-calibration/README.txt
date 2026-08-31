Current achievement calibration data

Snapshot
- Generated: 2026-08-31
- Achievement stages: 5453
- Total achievement points: 73910

This folder contains only the current achievement hierarchy extracted from:
- ACHIEVEMENT.json
- ACHIEVEMENT_CATEGORY.json
- ACHIEVEMENT_SUBCATEGORY.json
- ACHIEVEMENT_SECTION.json
- ACHIEVEMENT_SECTION_ITEM.json

Filtering rules
- Include only ACHIEVEMENT*.json records.
- Include only achievements where m_enabled = 1.
- Include only achievements that are still connected to the current hierarchy through ACHIEVEMENT_SECTION_ITEM.json.
- Exclude legacy ACHIEVE*.json data.

Files
- summary.json: filter rules and record counts.
- hierarchy.zh-CN.json: nested category -> subcategory -> section -> achievement tree with Chinese fields.
- flat.zh-CN.json: flattened rows with the same hierarchy path and Chinese text fields.
- extract_current_achievements.ps1: reproducible extraction script.