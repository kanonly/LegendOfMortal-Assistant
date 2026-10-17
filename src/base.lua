
-- LegendOfMortal / Unity Mono x64 / Cheat Engine 7.5
-- Self-contained: no external Lua file, game DLL patch, or BepInEx change.
if LOMCT and LOMCT.disable then LOMCT.disable() end
LOMCT = {version = '2.0', rows = {}, groups = {}, uiEntries = {}, enabled = false}
local T = LOMCT
local title = '启用／重新定位（先进入存档；取消勾选即关闭）'
local specs = {
  {3, '银两', '资源'}, {19, '行动次数', '资源'},
  {31, '武学点数', '资源'}, {20, '个人贡献度', '资源'},
  {0, '体力', '基础属性'}, {1, '内力', '基础属性'},
  {2, '轻功', '基础属性'}, {4, '魅力', '基础属性'},
  {5, '学问', '基础属性'}, {6, '心理卫生', '基础属性'},
  {12, '嘴力', '基础属性'}, {29, '防御', '基础属性'},
  {17, '锻造', '技能与抗性'}, {18, '毒药', '技能与抗性'},
  {21, '厨艺', '技能与抗性'}, {22, '抗毒', '技能与抗性'},
  {23, '抗麻', '技能与抗性'}
}

local function need(value, message)
  if value == nil or value == 0 then error(message, 0) end
  return value
end

local function int32(value)
  if value and value >= 0x80000000 then return value - 0x100000000 end
  return value
end

-- Zero is valid for stat values, unlike object pointers.
local function integer(address)
  local value = readInteger(address)
  if value == nil then error('无法读取属性整数，请关闭后重新启用。', 0) end
  return int32(value)
end

local function fields(class)
  local result = {}
  for _, f in ipairs(mono_class_enumFields(class, true)) do result[f.name] = f end
  return result
end

local function offset(map, name)
  if not map[name] then error('当前游戏缺少字段：' .. name, 0) end
  return map[name].offset
end

local function pointer(address, label)
  return need(readPointer(address), label .. ' 尚未初始化，请先进入存档。')
end

local function managedString(address)
  if not address or address == 0 then return '' end
  local length = readInteger(address + 0x10)
  if not length or length > 1024 then error('场景名称无法读取。', 0) end
  if length == 0 then return '' end
  return need(readString(address + 0x14, length * 2, true), '场景名称无法读取。')
end

local function group(description, parent)
  local record = getAddressList().createMemoryRecord()
  record.IsGroupHeader = true
  record.Description = description
  record.DontSave = true
  record.appendToEntry(parent)
  return record
end

-- Stable semantic IDs shared by the CT and the optional desktop front end.
function T.expose(record, metadata)
  metadata.record = record
  T.uiEntries[#T.uiEntries + 1] = metadata
end

local function connectMono(reconnect)
  if not LaunchMonoDataCollector then
    error('Mono 数据收集器启动失败，请确认 CE 的 autorun/monoscript.lua 完整。', 0)
  end
  if reconnect and monopipe then
    -- Drop an out-of-sync response stream; do not retry on the same pipe.
    local old = monopipe
    monopipe = nil
    mono_AttachedProcess, monoBase = 0, 0
    old.destroy()
  end
  need(LaunchMonoDataCollector(false), 'Mono 数据收集器启动失败，请确认 CE 的 Mono 收集器文件完整。')
end

function T.disable()
  local extensionOK, extensionError = true, nil
  if T.disableExtensions then extensionOK, extensionError = pcall(T.disableExtensions) end
  T.enabled = false
  for _, row in ipairs(T.rows) do row.record.Active = false end
  for _, record in ipairs(T.groups) do record.destroy() end
  T.rows, T.groups = {}, {}
  T.uiEntries = {}
  T.context = nil
  if T.root then T.root.Description = title end
  if not extensionOK then error(extensionError, 0) end
end

local function locate()
  local pid = need(getProcessIDFromProcessName('Mortal.exe'), '没有找到 Mortal.exe，请先启动游戏。')
  if getOpenedProcessID() ~= pid then openProcess(pid) end
  if getOpenedProcessID() ~= pid then error('无法附加 Mortal.exe。', 0) end
  if not targetIs64Bit() then error('本表适用于 64 位 Mortal.exe。', 0) end
  connectMono(true)
  local managerClass = need(mono_findClass('Mortal.Core', 'PlayerStatManagerData'), '找不到 PlayerStatManagerData。')
  local statClass = need(mono_findClass('Mortal.Core', 'GameStat'), '找不到 GameStat。')
  local mf, sf = fields(managerClass), fields(statClass)
  if not mf._instance then error('找不到主角单例字段 _instance。', 0) end
  local staticBase = need(mono_class_getStaticFieldAddress(0, managerClass), '找不到主角单例静态区。')
  local instanceSlot = staticBase + mf._instance.offset
  local manager = pointer(instanceSlot, '主角数据')
  local sceneOffset = offset(mf, '_currentScene')
  local sceneClass = need(mono_findClass('Mortal.Core', 'SceneController'), '找不到场景管理器。')
  local sceneFields = fields(sceneClass)
  local sceneInstanceField = sceneFields['<Instance>k__BackingField']
  if not sceneInstanceField then error('找不到场景管理器单例。', 0) end
  local sceneBase = need(mono_class_getStaticFieldAddress(0, sceneClass), '场景管理器未初始化。')
  local sceneManager = pointer(sceneBase + sceneInstanceField.offset, '场景管理器')
  local titleScene = managedString(readPointer(sceneManager + offset(sceneFields, '_title')))
  local currentScene = managedString(readPointer(manager + sceneOffset))
  if currentScene == '' or titleScene == '' or currentScene == titleScene then
    error('当前仍在标题画面，请先进入存档，加载完成后再启用。', 0)
  end
  local statsOffset = offset(mf, '_stats')
  local stats = pointer(manager + statsOffset, '主角属性集合')
  local statsClass = need(mono_object_getClass(stats), '无法解析主角属性集合。')
  local listOffset = offset(fields(statsClass), '_list')
  local list = pointer(stats + listOffset, '主角属性列表')
  local lf = fields(need(mono_object_getClass(list), '无法解析属性 List。'))
  local itemsOffset, sizeOffset = offset(lf, '_items'), offset(lf, '_size')
  local items = pointer(list + itemsOffset, '主角属性数组')
  local count = integer(list + sizeOffset)
  -- MonoArray x64 ABI: object + bounds + max_length; references begin at 0x20.
  local capacity = readQword(items + 0x18)
  if count < 1 or count > 1024 or not capacity or count > capacity then
    error('主角属性列表无效，请在存档加载完成后重新启用。', 0)
  end
  local typeOffset = offset(sf, '_type')
  local byType, indexes = {}, {}
  for i = 0, count - 1 do
    local object = readPointer(items + 0x20 + i * 8)
    if object and object ~= 0 then
      local kind = integer(object + typeOffset)
      if byType[kind] then error('属性编号重复，停止定位：' .. kind, 0) end
      byType[kind] = object
      indexes[kind] = i
    end
  end
  local selected = {}
  for _, spec in ipairs(specs) do
    local object = byType[spec[1]]
    if object then
      if mono_object_getClass(object) ~= statClass then error('属性对象类型不符：' .. spec[2], 0) end
      selected[#selected + 1] = {spec = spec, object = object, index = indexes[spec[1]]}
    end
  end
  need(byType[3], '当前集合中没有银两属性，请进入存档。')
  local method = need(mono_findMethod('Mortal.Core', 'GameStat', 'get_FinalValue'), '找不到最终值读取方法。')
  return {
    pid = pid, instanceSlot = instanceSlot, manager = manager,
    sceneOffset = sceneOffset, titleScene = titleScene,
    stats = stats, list = list, items = items, count = count,
    statsOffset = statsOffset, listOffset = listOffset,
    itemsOffset = itemsOffset, sizeOffset = sizeOffset,
    valueOffset = offset(sf, '_value'), minOffset = offset(sf, '_min'),
    maxOffset = offset(sf, '_max'), limitOffset = offset(sf, '_limitAddition'),
    typeOffset = typeOffset, finalMethod = method, selected = selected
  }
end

local function checkContext(c)
  if getOpenedProcessID() ~= c.pid or getProcessIDFromProcessName('Mortal.exe') ~= c.pid then
    error('游戏进程已关闭或切换。', 0)
  end
  if readPointer(c.instanceSlot) ~= c.manager
    or readPointer(c.manager + c.statsOffset) ~= c.stats
    or readPointer(c.stats + c.listOffset) ~= c.list
    or readPointer(c.list + c.itemsOffset) ~= c.items
    or readInteger(c.list + c.sizeOffset) ~= c.count then
    error('主角数据已改变，请重新定位。', 0)
  end
  local scene = managedString(readPointer(c.manager + c.sceneOffset))
  if scene == '' or scene == c.titleScene then error('已返回标题画面，请进入存档后重新启用。', 0) end
end

function T.refresh()
  -- CE may pump GUI messages while waiting for a Mono RPC. Avoid re-entry.
  if T.refreshing then return end
  T.refreshing = true
  local ok, err = pcall(function()
    local c = T.context
    checkContext(c)
    local function collect()
      local values = {}
      for i, row in ipairs(T.rows) do
        if integer(row.object + c.typeOffset) ~= row.kind then error('属性对象已改变，请重新定位。', 0) end
        local lo, hi = integer(row.object + c.minOffset), integer(row.object + c.maxOffset)
        local limit = integer(row.object + c.limitOffset)
        local final = mono_invoke_method(0, c.finalMethod, row.object, {})
        if final == nil then error('读取' .. row.name .. '最终值失败，请关闭后重新启用。', 0) end
        final = int32(final)
        if type(final) ~= 'number' or final < lo or final > hi + limit then
          error(row.name .. '最终值响应不符合游戏范围。', 0)
        end
        values[i] = {lo, hi, final, hi + limit}
      end
      return values
    end
    local readOK, values = pcall(collect)
    if not readOK then
      checkContext(c)
      connectMono(true)
      c.finalMethod = need(mono_findMethod('Mortal.Core', 'GameStat', 'get_FinalValue'), '重连后找不到最终值方法。')
      values = collect() -- One retry on a new connection; errors propagate.
    end
    for i, row in ipairs(T.rows) do
      local v = values[i]
      row.minimum, row.maximum, row.final = v[1], v[2], v[3]
      row.info.Description = string.format('只读（刷新时）：基础范围 %d ～ %d；最终值 %d；最终上限 %d', v[1], v[2], v[3], v[4])
    end
  end)
  T.refreshing = false
  if not ok then error(tostring(err), 0) end
end

function T.enable(root)
  T.disable()
  T.root = root
  local ok, err = pcall(function()
    local c = locate()
    T.context = c
    local parents = {}
    for _, entry in ipairs(c.selected) do
      local spec, object = entry.spec, entry.object
      if not parents[spec[3]] then
        parents[spec[3]] = group(spec[3], root)
        T.groups[#T.groups + 1] = parents[spec[3]]
      end
      local record = getAddressList().createMemoryRecord()
      record.DontSave = true
      record.Description = spec[2] .. '（基础值）'
      record.Type = vtDword
      record.ShowAsSigned = true
      record.Address = string.format('%X', c.instanceSlot)
      record.OffsetCount = 5
      record.Offset[0] = c.valueOffset
      record.Offset[1] = 0x20 + entry.index * 8
      record.Offset[2] = c.itemsOffset
      record.Offset[3] = c.listOffset
      record.Offset[4] = c.statsOffset
      record.appendToEntry(parents[spec[3]])
      local row = {record = record, info = group('只读：正在读取…', record), object = object, kind = spec[1], name = spec[2]}
      T.rows[#T.rows + 1] = row
      T.expose(record, {id = 'stat:' .. spec[1], name = spec[2], section = 'stats', category = spec[3], kind = 'stat', stat = row})
      record.OnActivate = function(_, before)
        if before then
          local valid = pcall(checkContext, c)
          return T.enabled and valid
        end
        return true
      end
      record.OnValueChangedByUser = function(r)
        -- Respect the game's base range, without changing _min/_max.
        local lo, hi = integer(object + c.minOffset), integer(object + c.maxOffset)
        local value = integer(object + c.valueOffset)
        local clamped = math.max(lo, math.min(hi, value))
        if clamped ~= value then r.Value = tostring(clamped) end
      end
    end
    T.refresh()
    T.enabled = true
    if T.extend then T.extend(root, c) end
    root.Description = '已启用：' .. #T.rows .. ' 项属性及旧表扩展（取消勾选可关闭）'
    root.Collapsed = false
  end)
  if not ok then T.disable(); error(tostring(err), 0) end
end

-- Dynamic records contain runtime addresses and must never be saved in the CT.
local root = getAddressList().getMemoryRecordByID(0)
if root then
  -- Native Lua callbacks manage the table lifecycle without an assembly patch.
  root.OnActivate = function(record, before)
    if before then
      local ok, err = pcall(T.enable, record)
      if not ok then
        record.Description = '启用失败：' .. tostring(err)
        return false
      end
    end
    return true
  end
  root.OnDeactivate = function(_, before)
    if before then T.disable() end
    return true
  end
  root.OnDestroy = function() T.disable(); T.root = nil end
end
local refreshEntry = getAddressList().getMemoryRecordByID(2)
if refreshEntry then
  refreshEntry.OnActivate = function(_, before)
    if before then
      if not T.enabled then
        refreshEntry.Description = '刷新失败：请先勾选第一行启用本表。'
      else
        local ok, err = pcall(T.refresh)
        if not ok then
          if root.Active then root.Active = false else T.disable() end
        end
        refreshEntry.Description = ok and '刷新只读最终值（改值或更换装备后点击）' or ('刷新失败：' .. tostring(err))
      end
      return false
    end
    return true
  end
end
