"""Generate the user-facing support list from current translation data.

Aliases below identify the same mod across locale/UI/F12 profiles. They are
documentation metadata only and never change runtime profile IDs or mod credits.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
# Exact config filenames -> stable mod_id, or readable F12-only name.
CONFIG = dict(line.split('|', 1) for line in '''7Bpencil.TransparentSights|smallui.transparentsights
7Bpencil.WeaponCamoAndStickers|7Bpencil.WeaponCamoAndStickers.UI
com.20fpsguy.parmesan|Parmesan - Partizan Rework
com.acidphantasm.botplacementsystem|BotPlacementSystem
com.acidphantasm.brightlasers|Bright Lasers
com.acidphantasm.previewsizer|Preview Sizer
com.alanyung-yl.holstereverything.f12config|smallui.holstereverything
com.Amanda.Graphics|AmandsGraphics
com.awnova.raidsettingsskipper|Fika Raid Settings Skipper
com.blackhawk.dynamicmapsextended|Dynamic Maps Extended
com.borkel.nvgmasks|Realistic NVG
com.c11.tn4client|mod_700ec7e0d1d4
com.chazut.orbit|ORBIT
com.choochoo.tradermodding|ChooChoo.TraderModding
com.cj.useFromAnywhere|smallui.useitemsanywhere
com.danw.questingbots|QuestingBots
com.danw.questingbotscustombotgenexample|QuestingBots
com.deadlauncher.FlexibleBuilds|Flexible Builds
com.flir.increaselookdirection|Increase Look Direction
com.fontaine.fovfix|Fontaine's FOV Fix
com.harmonyzt.breakoutoutlines|Breakout Outlines
com.harmonyzt.BringBackConcussion|Bring Back Concussion
com.harmonyzt.MusicExtender|Music Extender
com.hysocs.adaptivebotculling|Adaptive Bot Culling
com.hysocs.cinekit|smallui.cinekit
com.hysocs.ragdollkinetics|Ragdoll Kinetics
com.hysocs.traumacore|smallui.traumacore
com.inku.inspectionlessmalfsreborn|Inspectionless Malfunctions Reborn
com.janky.hollywoodfx|HollywoodFX
com.jbobyh.itempreviewqol|ItemPreviewQoL
com.kaeno.TraderScrolling|Trader Scrolling
com.lacyway.ch|Continuous Healing
com.lacyway.csf|QuickSellFlea
com.lacyway.hanb|Hands Are Not Busy
com.lennoxp90.coti|mod_3b2b877cc80c
com.lennoxp90.mapvariants|smallui.mapvariants
com.liquidwarp.munitionsexpert|smallui.munitionsexpert
com.manimal.csgas|mod_846ff7f03e30
com.manimal.icebreaker.fika|Icebreaker
com.manimal.icebreaker|Icebreaker
com.manimal.interchange|mod_69c5ddfe1691
com.manimal.labsboiler|Manimal's Labs Boiler
com.manimal.lighthouse|mod_3cd5f301d815
com.manimal.questbriefingapi|smallui.questbriefingapi
com.maschine.AutoCorpseSearch|Auto Corpse Search
com.maschine.EasyMounting|Easy Mounting
com.maschine.UnloadAllMagazines|UnloadAllMagazines
com.maschine.WeaponBuilderSearch|smallui.weaponbuildersearch
com.morebotsapi.tacticaltoaster|MoreBotsAPI
com.moxopixel.hideoutshootout|Hideout Shootout
com.moxopixel.menuoverhaul|smallui.menuoverhaul
com.mpstark.dynamicmaps|DynamicMaps
com.mpstark.PlayerEncumbranceBar|Player Encumbrance Bar
com.mybutthasarash.sptcasino|com.mybutthasarash.sptcasino
com.ozen.continuousloadammo|ContinuousLoadAmmo
com.ozen.foldables|com.ozen.foldables
com.ozen.magcheckinterrupt|MagCheckInterrupt
com.pein.battleambience|Battle Ambience
com.pein.camerarecoilmod|Recoil Rework
com.pein.shadowflickerfix|Shadow Flicker Fix
com.ruafcomehome.tacticaltoaster|com.ruafcomehome.tacticaltoaster
com.shaneeexd.thequartermaster|TheQuartermaster
com.Shibatsu.DynamicExternalResolution|Dynamic External Resolution
com.smajlec.lights|Smajlec Lights
com.smajlec.stamina|Smajlec Stamina
com.sora.visitapi|com.sora.visitapi
com.suomi.makshepard.smprt|source3.suomi_prt
com.Tangh.CookingGrenades|CookingGrenades
com.tarkin.doordash|DoorDash
com.tarkin.hideoutcat|smallui.hideoutcat
com.tarkin.huir|smallui.hideoutuirevamp
com.terkoiz.freecam|Freecam
com.terkoiz.skipper|smallui.skipper
com.Tetris.DeHazardifier|DeHazardifier
com.trenchfoot.beltslot|mod_b776e1dd87a1
com.tyfon.autodeposit|Auto Deposit
com.tyfon.autorun|Auto Run
com.tyfon.uifixes|UIFixes
com.tyfon.weaponcustomizer|WeaponCustomizer
com.tyfon.wikilinks|smallui.wikilinks
com.tylevo.tacticalservicescontrol|com.tylevo.tacticalservicescontrol
com.vinihns.makeMedsGreatAgain|Make Meds Great Again!
com.vinihns.makeshotgunsgreatagain|source3.make_shotguns_great_again
com.vultify.nvgsightdimmer|NVG Sight Dimmer
com.wtt.armory|mod_51b0c77c9951
com.wtt.commonlib|smallui.wttcommonlib
dev.oogabooga.spt-vagabond|Vagabond
ekky.raidreview|RaidReview
flir.iof|InventoryOrganizingFeatures
hazelify.BushWhackerStandalone|BushWhacker Standalone
hazelify.StanceSync|StanceSync
HealingAutoCancel|Makes Healing Less Dumb
katrin0522.FastSellInFlea|FastSellInFlea
Mattexe.BossNotifier|BossNotifier
me.skwizzy.lootingbots|Looting Bots
me.sol.sain|smallui.sain
moxopixel.advanced.modding.lights|Advanced Modding Lights
xyz.drakia.quickmovetocontainer|Quick Move To Container
xyz.drakia.Sense|Sense
xyz.drakia.waypoints|Waypoints
xyz.pit.fireteam|pitfireteam.trader.locale'''.splitlines())

def load(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))

def main():
    mods = {}
    for p in sorted((ROOT/'translations').glob('*.json')):
        doc=load(p)
        channels={c:rs for c,rs in doc['channels'].items() if c!='reference_only' and any(r.get('enabled', True) for r in rs)}
        if not channels: continue
        scopes=[]
        if any(c in channels for c in ('locale','locale_source_fallback','locale_clone_suffix')): scopes.append('로케일')
        if any(c in channels for c in ('small_ui','custom_ui','map_content','locale_additions','coti_dpad','ui_literals','external_ui_literals','equipment_placements','horse_racing_ui','casino_war_ui','dialogs')): scopes.append('UI')
        if any(c in channels for c in ('notifications','notification_terms','error_screens','server_errors')): scopes.append('알림·오류')
        if any(c in channels for c in ('npc_messages','server_messages')): scopes.append('직접 메시지')
        mods[doc['mod_id']]={'name':doc['mod_name'].replace(' (상인정보만 번역)',''),'scope':scopes,'files':[p.relative_to(ROOT).as_posix()]}
    config_files=[p for p in sorted((ROOT/'config-ui').glob('*.json')) if not p.name.startswith('_')]
    for p in config_files:
        assert p.stem in CONFIG, 'Review the identity of new config profile: '+p.name
        doc=load(p)
        assert doc.get('entries') or doc.get('categories'), 'Empty configuration profile: '+p.name
        key=CONFIG[p.stem]
        entry=mods.setdefault(key,{'name':key,'scope':[],'files':[]})
        if 'F12 설정' not in entry['scope']: entry['scope'].append('F12 설정')
        entry['files'].append(p.relative_to(ROOT).as_posix())
    pit=mods['pitfireteam.trader.locale']
    for lang in ('krx','kren'):
        p=ROOT/'native-locales'/'pitfireteam'/(lang+'.json')
        assert p.exists()
        pit['files'].append(p.relative_to(ROOT).as_posix())
    pit['scope'].insert(1,'자체 UI 언어팩')
    notes={
        'mod_b03daac1dc68':'상인 정보만 번역',
        'DynamicMaps':'전용 UI 훅은 1.2.1 대상',
        'QuestingBots':'Custom Bot Generation Example 설정 포함',
        'Icebreaker':'Fika 연동 설정 포함',
        '7Bpencil.WeaponCamoAndStickers.UI':'Equipment Stickers 관련 UI 포함',
        'smallui.sain':'에디터 UI 및 F12 설정',
    }
    main_rows=sorted((k for k,v in mods.items() if v['scope']!=['F12 설정']),key=lambda k:mods[k]['name'].casefold())
    f12_rows=sorted((k for k,v in mods.items() if v['scope']==['F12 설정']),key=lambda k:mods[k]['name'].casefold())
    version=re.search(r'<Version>(.*?)</Version>',(ROOT/'SPT_Mod_Korean_Addon.csproj').read_text(encoding='utf-8-sig')).group(1)
    lines=[f'# 번역 지원 모드 목록 — {version}','','SPT 4.1.6용 SPT Mod Korean Addon의 현재 데이터 기준 목록입니다. 부모 모드 SPT Korean Project - Locale Patcher가 필요합니다.','',
        f'**총 {len(mods)}개 지원 항목**: 게임 내 텍스트·UI·메시지 등을 지원하는 {len(main_rows)}개와 F12 설정만 지원하는 {len(f12_rows)}개입니다. 같은 모드의 클라이언트·서버·연동 구성요소는 한 항목으로 묶었습니다. Weapon Camo & Stickers와 Equipment Stickers처럼 함께 처리하는 관련 모드 묶음도 있으므로 개별 플러그인 수와 다릅니다.','',
        '목록에 있다는 것은 아래 범위의 번역 데이터가 있다는 뜻이며, 해당 모드의 모든 문장·모든 버전을 완역했다는 뜻은 아닙니다. 실제 적용은 모드 버전, 원문 일치, 기존 훅의 지원 범위에 따라 달라집니다.','',
        '- **로케일**: 해당 모드에 등록된 아이템·의류·퀘스트·상인·업적 등의 텍스트. 모든 모드가 이 종류를 전부 포함하는 것은 아닙니다.',
        '- **UI**: 등록된 버튼·툴팁·에디터·지도·팝업 등.',
        '- **알림·오류**: 등록된 화면 알림과 오류 문구.',
        '- **직접 메시지**: 등록된 NPC/시스템 직접 메시지. 이미 저장된 우편이 소급 번역된다는 뜻은 아닙니다.',
        '- **F12 설정**: 설정 이름·설명·분류·선택지. 모드 전체 UI 번역을 뜻하지 않습니다.','',
        '## 게임 내 번역 지원','','| 모드 | 번역 범위 | 비고 |','| --- | --- | --- |']
    for k in main_rows:
        v=mods[k];lines.append(f"| {v['name']} | {', '.join(v['scope'])} | {notes.get(k,'')} |")
    lines+=['','## F12 설정만 번역하는 모드','','아래 모드의 게임 내 UI·대사까지 번역한다고 안내하지 않습니다.','']
    lines+=['- '+mods[k]['name'] for k in f12_rows]
    lines+=['','## 목록 관리','','`translations/*.json`, `config-ui/*.json`, `native-locales/pitfireteam`을 기준으로 생성했습니다. 참고 전용 reference_only는 지원 범위에 포함하지 않습니다. 일반 F12 CustomDrawer 등 코드 전용 보조 번역은 위 해당 모드의 범위에 포함하며 별도 모드로 중복 집계하지 않습니다.','',
        '`tools/build_supported_mods.py`를 실행하면 이 목록을 갱신합니다. 새 F12 프로필은 스크립트의 문서용 별칭 표에 실제 소속 모드를 등록해야 하며, 런타임 mod_id나 plugin GUID를 바꾸는 작업은 아닙니다. 웹 UI 전체 번역은 이 목록의 보장 범위에 포함하지 않습니다.']
    (ROOT/'docs'/'번역지원모드목록.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(f'{version}: {len(mods)} entries; game text {len(main_rows)}, F12 only {len(f12_rows)}; {len(config_files)} F12 profiles mapped.')

if __name__=='__main__': main()
