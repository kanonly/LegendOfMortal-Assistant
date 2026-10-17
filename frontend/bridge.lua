-- Appended to the shared CT Lua. No downloaded or legacy scripts are executed.
local U = {generation = 0, entries = {}, features = {}, byId = {}, busy = false}
LOMUI = U
-- CE 7.5 injectLibrary uses LoadLibraryA. A UTF-8 Chinese path can fall through
-- to manual mapping; that breaks native C++ exception unwinding on disconnect.
-- Use Windows' Unicode loader so the collector has normal loader/unwind state.
local originalInjectLibrary = injectLibrary
injectLibrary = function(path, skipsymbols)
  if type(path) == 'string' and path:lower():match('monodatacollector64%.dll$') then
    local loaded = false
    for _, module in ipairs(enumModules()) do
      if module.Name:lower() == 'monodatacollector64.dll' then loaded = true; break end
    end
    if not loaded then
      local handle = executeCodeEx(0, 10000, 'kernel32.LoadLibraryW', {type=4, value=path})
      if not handle or handle == 0 then return false, '无法通过 Unicode 路径加载 Mono 采集器。' end
    end
    if not skipsymbols then reinitializeSymbolhandler(); waitForExports() end
    return true
  end
  return originalInjectLibrary(path, skipsymbols)
end
U.harmonyStatus = '尚未启用增强'
U.harmonyCompatible = true
T.prepareEnhancements = function()
  local path = LOMUI_CONFIG.runtimeDir .. '/Bootstrap.dll'
  local assembly = need(mono_loadAssemblyFromFile(path), '无法加载内嵌依赖引导层。')
  local image = need(mono_getImageFromAssembly(assembly), '无法读取依赖引导层。')
  local class = need(mono_image_findClass(image, 'LOMCT.Loader', 'Bootstrap'), '依赖引导入口缺失。')
  local method = need(mono_class_findMethod(class, 'Prepare'), '依赖准备入口缺失。')
  local ok = mono_invoke_method(0, method, 0, {})
  local describe = need(mono_class_findMethod(class, 'Describe'), '依赖状态入口缺失。')
  U.harmonyStatus = tostring(mono_invoke_method(0, describe, 0, {}) or '依赖状态未知')
  U.harmonyCompatible = ok == 1
  if not U.harmonyCompatible then error(U.harmonyStatus, 0) end
end
local function array(t) return setmetatable(t or {}, {isArray = true}) end
local function quote(s)
  return '"' .. tostring(s):gsub('[%z\1-\31\\"]', function(c)
    return ({['"']='\\"', ['\\']='\\\\', ['\n']='\\n', ['\r']='\\r', ['\t']='\\t'})[c] or string.format('\\u%04x', c:byte())
  end) .. '"'
end
local function json(v)
  if v == nil then return 'null' end
  if type(v) == 'string' then return quote(v) end
  if type(v) == 'number' or type(v) == 'boolean' then return tostring(v) end
  local parts = {}
  if getmetatable(v) and getmetatable(v).isArray then
    for _, item in ipairs(v) do parts[#parts + 1] = json(item) end
    return '[' .. table.concat(parts, ',') .. ']'
  end
  for k, item in pairs(v) do parts[#parts + 1] = quote(k) .. ':' .. json(item) end
  return '{' .. table.concat(parts, ',') .. '}'
end
local function feature(id, name, category, off, bit, mult, default, tested)
  local f = {id = 'feature:' .. id, name = name, section = 'features', category = category, kind = 'feature',
    off = off, bit = bit, multiplierOffset = mult, multiplierDefault = default or 1,
    tested = tested or '待实战验证', minimum = 1, maximum = 1000}
  U.features[#U.features + 1] = f
  return f
end
feature('alchemyBudget', '炼丹升级额度不减反增', '养成', 16, 2)
feature('martialBudget', '修炼额度不减反增', '养成', 20, 3)
feature('alchemySect', '炼丹开发无门派规模要求', '养成', 24, 4, nil, nil, '实际对象 getter 已验证；完整开发待测')
feature('forgeSect', '锻造开发无门派规模要求', '养成', 28, 4, nil, nil, '实际对象 getter 已验证；完整开发待测')
for _, spec in ipairs(specs) do
  feature('invert:' .. spec[1], spec[2] .. '不减反增', '属性变化', 1024 + spec[1] * 4, 0,
    4096 + spec[1] * 4, 1, spec[1] == 3 and '银两实际变化已验证' or '共用变化入口已验证；本项未逐项触发')
end
local relation = feature('relationship', '好感度增长加倍', '好感', 32, 1, 36, 10, '正向倍率与负向反转已验证')
relation.secondaryOffset = 40
feature('battleCount', '我方人数不减', '战役', 68, 7)
feature('battleHealth', '主角生命不减反增', '战役', 48, 5)
feature('battleMana', '主角内力不减反增', '战役', 52, 6)
feature('battleEnemyMana', '敌人内力快速清零', '战役', 56, 6)
feature('battleDamage', '对敌伤害加倍', '战役', 60, 5, 64, 10)
feature('duelHealth', '主角生命不减反增', '决斗', 80, 8)
feature('duelMana', '主角内力不减反增', '决斗', 84, 9)
feature('duelEnemyMana', '敌人内力快速清零', '决斗', 88, 9)
feature('duelDamage', '对敌伤害加倍', '决斗', 92, 8, 96, 10)
local dice = feature('dice', '固定天命轮盘点数', '轮盘', 112, 10, 116, 50)
dice.minimum, dice.maximum = 0, 100

local function enabledFeature(f)
  return E.config and readInteger(E.config + f.off) == 1 or false
end
local function snapshot(e)
  local s = {id=e.id, name=e.name, section=e.section, category=e.category, kind=e.kind,
    parentId=e.parentId or '', note=e.note or '', available=true, tested=e.tested or '',
    minimum=e.minimum or -2147483648, maximum=e.maximum or 2147483647}
  if e.kind == 'feature' then
    s.enabled = enabledFeature(e)
    s.hasParameter = e.multiplierOffset ~= nil
    s.parameter = E.config and e.multiplierOffset and int32(readInteger(E.config + e.multiplierOffset)) or e.multiplierDefault
    s.hasSecondary = e.secondaryOffset ~= nil
    s.secondary = not E.config or not e.secondaryOffset or readInteger(E.config + e.secondaryOffset) == 1
    s.available = U.harmonyCompatible and (not E.config or math.floor(E.capabilities / 2 ^ e.bit) % 2 == 1)
  else
    s.value = tonumber(e.record.Value)
    if s.value == nil then error('数值无法读取，请重新定位：' .. e.name, 0) end
    s.frozen = e.record.Active
    if e.stat then
      local c = T.context
      s.minimum = integer(e.stat.object + c.minOffset)
      s.maximum = integer(e.stat.object + c.maxOffset)
      s.finalValue = e.stat.final
      s.finalMaximum = s.maximum + integer(e.stat.object + c.limitOffset)
    end
  end
  return s
end
local function rebuildCatalog()
  U.entries, U.byId = {}, {}
  for _, list in ipairs({T.uiEntries, U.features}) do
    for _, e in ipairs(list) do U.entries[#U.entries + 1] = e; U.byId[e.id] = e end
  end
end
local function ensureScripts()
  if not E.config then
    E.scripts.Active = true
    if not E.config or not E.scripts.Active then error(E.scripts.Description, 0) end
  end
end
local function stop()
  U.generation = U.generation + 1
  local root = getAddressList().getMemoryRecordByID(0)
  if root and root.Active then root.Active = false end
  T.disable()
  U.entries, U.byId = {}, {}
end
local function checked()
  if not T.enabled then error('未连接存档，请点击重新定位。', 0) end
  local ok, err = pcall(checkContext, T.context)
  if not ok then pcall(stop); error(err, 0) end
end
local function process(op, id, arg, number, state, generation, offset)
  if op == 'status' then
    if T.enabled then checked() end
    return {message = T.enabled and '已连接存档' or '等待游戏与存档'}
  end
  if op == 'stop' then stop(); return {message='全部冻结与增强已停止；已改数值保留。'} end
  if op == 'connect' then
    stop()
    U.harmonyCompatible, U.harmonyStatus = true, '尚未启用增强'
    local root = getAddressList().getMemoryRecordByID(0)
    root.Active = true
    if not T.enabled then error(root.Description, 0) end
    rebuildCatalog()
    return {message='已定位，全部功能默认关闭。', total=#U.entries}
  end
  checked()
  if generation ~= U.generation then error('操作所属的存档连接已过期，请重新定位。', 0) end
  if op == 'catalog' then
    local list = array()
    local matched = {}
    for _, e in ipairs(U.entries) do if id == '' or e.section == id then matched[#matched + 1] = e end end
    for i = offset + 1, math.min(offset + 40, #matched) do list[#list + 1] = snapshot(matched[i]) end
    return {entries=list, total=#matched}
  end
  if op == 'read' then
    local list = array()
    for entryId in id:gmatch('[^\n]+') do
      local e = U.byId[entryId]
      if e then list[#list + 1] = snapshot(e) end
    end
    return {entries=list}
  end
  if op == 'refresh' then T.refresh(); return {message='最终值已刷新。'} end
  local e = U.byId[id]
  if not e then error('当前连接中不存在该项目。', 0) end
  if op == 'set' then
    if e.kind == 'feature' then error('增强功能不能当作普通数值写入。', 0) end
    local info = snapshot(e)
    if number % 1 ~= 0 or number < info.minimum or number > info.maximum then
      error(string.format('请输入 %d ～ %d 范围内的整数。', info.minimum, info.maximum), 0)
    end
    -- Value assignments from Lua do not fire OnValueChangedByUser.
    e.record.Value = tostring(number)
    if tonumber(e.record.Value) ~= number then error('写入结果未确认，请重新读取。', 0) end
  elseif op == 'freeze' then
    if e.kind == 'feature' then error('增强开关不使用数值冻结。', 0) end
    e.record.Active = state
    if e.record.Active ~= state then error('冻结切换未确认。', 0) end
  elseif op == 'feature' then
    if e.kind ~= 'feature' then error('不是增强项目。', 0) end
    ensureScripts()
    if not snapshot(e).available then error('当前游戏版本的该增强入口不可用。', 0) end
    if arg == 'enabled' then
      local r = assert(E.toggles[e.off])
      r.Active = state
      if enabledFeature(e) ~= state then error('增强开关切换未确认。', 0) end
    elseif arg == 'parameter' and e.multiplierOffset then
      if number % 1 ~= 0 or number < e.minimum or number > e.maximum then error('参数超出允许范围。', 0) end
      assert(writeInteger(E.config + e.multiplierOffset, number))
    elseif arg == 'secondary' and e.secondaryOffset then
      assert(writeInteger(E.config + e.secondaryOffset, state and 1 or 0))
    else error('无效的增强参数。', 0) end
  else error('未知操作：' .. tostring(op), 0) end
  return {entries=array({snapshot(e)}), message='已应用。'}
end

function LOMUI_Request(op, id, arg, number, state, generation, offset)
  if U.busy then return json({ok=false, error='后台正在处理上一项操作。', connected=T.enabled, generation=U.generation}) end
  U.busy = true
  local ok, result = pcall(process, op, id or '', arg or '', number or 0, state or false, generation or -1, offset or 0)
  U.busy = false
  local response = ok and result or {error=tostring(result)}
  response.ok, response.connected, response.generation = ok, T.enabled, U.generation
  response.pid = T.context and T.context.pid or 0
  response.enhanced = E.config ~= nil
  response.backendVersion = LOMUI_CONFIG.bundleVersion
  response.harmonyStatus, response.harmonyCompatible = U.harmonyStatus, U.harmonyCompatible
  return json(response)
end

-- Only this application owns this backend. A closed/crashed front end cannot
-- leave its invisible CE process applying freezes indefinitely.
local ownerTimer = createTimer(nil, true)
ownerTimer.Interval = 3000
ownerTimer.OnTimer = function()
  if U.busy then return end
  local list = getProcessList()
  if not list[LOMUI_CONFIG.ownerPid] then
    ownerTimer.Enabled = false
    pcall(stop)
    getApplication().terminate()
  end
end
openLuaServer(LOMUI_CONFIG.pipe)
