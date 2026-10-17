-- Migration layer. The old CT is only a feature reference, never executable input.
local E = {records = {}, configRecords = {}, fieldCache = {}, bridge = nil, toggles = {}, parameters = {}}
T.legacy = E
local relationNames = {
__RELATION_NAMES__
}
local runtimeName = '__ASSEMBLY_NAME__'
local runtimeHex = '__RUNTIME_HEX__'

local function mapFor(object)
  local class = need(mono_object_getClass(object), '无法解析扩展数据对象。')
  if not E.fieldCache[class] then E.fieldCache[class] = fields(class) end
  return E.fieldCache[class]
end
local function top(description, parent)
  local r = group(description, parent)
  T.groups[#T.groups + 1] = r
  return r
end
local function valueRecord(parent, description, base, forward, vartype)
  local r = getAddressList().createMemoryRecord()
  r.DontSave = true
  r.Description = description
  r.Type = vartype or vtDword
  r.ShowAsSigned = true
  r.Address = string.format('%X', base)
  r.OffsetCount = #forward
  for i, v in ipairs(forward) do r.Offset[#forward - i] = v end
  r.appendToEntry(parent)
  E.records[#E.records + 1] = r
  return r
end
local function childChain(prefix, a, b)
  local t = {}
  for _, v in ipairs(prefix) do t[#t + 1] = v end
  t[#t + 1] = a
  if b then t[#t + 1] = b end
  return t
end
local function enumRecord(r, text)
  r.DropDownList.Text = text
  r.DropDownReadOnly = true
  r.DropDownDescriptionOnly = true
  r.DisplayAsDropDownListItem = true
end
local function listData(collection)
  local listOff = offset(mapFor(collection), '_list')
  local list = pointer(collection + listOff, '扩展列表')
  local lf = mapFor(list)
  local itemsOff = offset(lf, '_items')
  local items = pointer(list + itemsOff, '扩展数组')
  local count = integer(list + offset(lf, '_size'))
  local capacity = readQword(items + 0x18)
  if count < 0 or count > 10000 or not capacity or count > capacity then error('扩展列表长度无效。', 0) end
  return {list = list, items = items, count = count, prefix = {listOff, itemsOff}}
end
local function collectionRecords(parent, baseSlot, owner, collectionField, kind)
  local collectionOff = offset(mapFor(owner), collectionField)
  local collection = pointer(owner + collectionOff, collectionField)
  local list = listData(collection)
  local prefix = {collectionOff, list.prefix[1], list.prefix[2]}
  local first = list.count > 0 and readPointer(list.items + 0x20) or 0
  if first == 0 then group('当前列表为空', parent); return end
  local f = mapFor(first)
  local countOff = offset(f, kind == 'relationship' and '_value' or '_currentCount')
  for i = 0, list.count - 1 do
    local object = readPointer(list.items + 0x20 + i * 8)
    if object and object ~= 0 then
      local label, id
      if kind == 'relationship' then
        id = integer(object + offset(f, '_type'))
        label = relationNames[id] or ('人物编号 ' .. id)
      else
        id = managedString(readPointer(object + offset(f, '_id')))
        local note = f._devNote and managedString(readPointer(object + f._devNote.offset)) or ''
        label = (note ~= '' and note or id) .. ' [' .. id .. ']'
      end
      local r = valueRecord(parent, label, baseSlot, childChain(prefix, 0x20 + i * 8, countOff))
      local stable = kind == 'relationship' and ('relation:' .. id) or ('item:' .. kind .. ':' .. id)
      local section = kind == 'relationship' and 'relations' or 'items'
      T.expose(r, {id = stable, name = label, section = section, category = kind, kind = kind,
        minimum = 0, maximum = kind == 'relationship' and integer(object + offset(f, '_max')) or 2147483647})
      if kind == 'relationship' then
        local active = valueRecord(r, '已结识／显示', baseSlot, childChain(prefix, 0x20 + i * 8, offset(f, '_active')), vtByte)
        T.expose(active, {id = stable .. ':active', parentId = stable, name = '已结识／显示', section = section, category = kind, kind = 'boolean', minimum = 0, maximum = 1})
        enumRecord(active, '0:否\n1:是')
        group('好感上限：' .. integer(object + offset(f, '_max')), r)
      elseif kind == 'book' then
        local level = valueRecord(r, '当前修炼等级（直改不追补升级奖励）', baseSlot, childChain(prefix, 0x20 + i * 8, offset(f, '_currentMartialLevel')))
        local points = valueRecord(r, '当前修炼点数', baseSlot, childChain(prefix, 0x20 + i * 8, offset(f, '_currentLevelPoint')))
        T.expose(level, {id = stable .. ':level', parentId = stable, name = '当前修炼等级', section = section, category = kind, kind = 'level', minimum = 0, maximum = 2147483647, note = '直接改等级不追补升级奖励。'})
        T.expose(points, {id = stable .. ':points', parentId = stable, name = '当前修炼点数', section = section, category = kind, kind = 'points', minimum = 0, maximum = 2147483647})
      end
      r.Collapsed = true
    end
  end
  parent.Collapsed = true
end

local function invokeBridge(name)
  local b = need(E.bridge, '脚本增强尚未加载。')
  if getOpenedProcessID() ~= b.pid or getProcessIDFromProcessName('Mortal.exe') ~= b.pid then
    error('游戏进程已改变，请重新启用本表。', 0)
  end
  local method = b.methods[name]
  if not method then
    method = need(mono_class_findMethod(b.class, name), '缺少运行时方法：' .. name)
    b.methods[name] = method
  end
  return mono_invoke_method(0, method, 0, {})
end
local function loadBridge()
  connectMono(true)
  if T.prepareEnhancements then T.prepareEnhancements() end
  local bytes = {}
  for pair in runtimeHex:gmatch('%x%x') do bytes[#bytes + 1] = tonumber(pair, 16) end
  local path = getTempFolder() .. runtimeName .. '.dll'
  -- A loaded managed assembly may keep its file open on Windows. Its name is
  -- content-versioned by the builder, so reuse it instead of overwriting it.
  if not fileExists(path) then
    local stream = createMemoryStream()
    stream.write(bytes)
    stream.saveToFile(path)
    stream.destroy()
  end
  local assembly = need(mono_loadAssemblyFromFile(path), '无法加载内嵌运行时。')
  local image = need(mono_getImageFromAssembly(assembly), '无法读取运行时程序集。')
  local class = need(mono_image_findClass(image, 'LOMCT.Runtime', 'Control'), '无法找到运行时入口。')
  E.bridge = {pid = getOpenedProcessID(), class = class, methods = {}}
  local address = need(invokeBridge('Attach'), '运行时补丁安装失败。')
  if readInteger(address) ~= 0x4C4F4D32 or readInteger(address + 4) ~= 1 then
    error('运行时配置块校验失败。', 0)
  end
  E.config = address
  E.capabilities = readInteger(address + 12)
  return address
end
local function has(bit)
  return math.floor(E.capabilities / 2 ^ bit) % 2 == 1
end
local function configValue(parent, description, off, valueType)
  local r = valueRecord(parent, description, E.config + off, {}, valueType)
  table.remove(E.records) -- Config records are destroyed when their group closes.
  E.configRecords[#E.configRecords + 1] = r
  E.parameters[off] = r
  return r
end
local function toggle(parent, description, off, capability)
  local r = group(description, parent)
  E.toggles[off] = r
  E.configRecords[#E.configRecords + 1] = r
  if not has(capability) then
    r.Description = '[当前版本入口不可用] ' .. description
    r.OnActivate = function() return false end
    return r
  end
  r.OnActivate = function(_, before)
    if before then
      if not T.enabled or not E.config or getOpenedProcessID() ~= E.bridge.pid then return false end
      return writeInteger(E.config + off, 1)
    end
    return true
  end
  r.OnDeactivate = function(_, before)
    if before and E.config and getOpenedProcessID() == E.bridge.pid then writeInteger(E.config + off, 0) end
    return true
  end
  return r
end
local function stopScripts()
  for _, r in ipairs(E.configRecords) do r.Active = false end
  E.configRecords = {}
  E.toggles, E.parameters = {}, {}
  if E.bridge and getOpenedProcessID() == E.bridge.pid and getProcessIDFromProcessName('Mortal.exe') == E.bridge.pid then
    -- Deactivate the shared block first, even if a Mono RPC has failed.
    if E.config then writeInteger(E.config + 4, 0) end
    local ok, result = pcall(invokeBridge, 'Detach')
    if not ok or result ~= 1 then
      connectMono(true)
      result = invokeBridge('Detach')
      if result ~= 1 then error('增强开关已关闭，但撤销补丁失败；请重启游戏。', 0) end
    end
  end
  E.config = nil
  if E.scripts then
    while E.scripts.Count > 0 do E.scripts.Child[0].destroy() end
  end
end

local function startScripts(parent)
  stopScripts()
  local ok, err = pcall(function()
    loadBridge()
    local budgets = group('升级／开发', parent)
    toggle(budgets, '炼丹最大消费不减反增（升级额度）', 16, 2)
    toggle(budgets, '修炼最大消费不减反增（修炼额度）', 20, 3)
    toggle(budgets, '炼丹开发无门派规模要求', 24, 4)
    toggle(budgets, '锻造开发无门派规模要求', 28, 4)
    local statGroup = group('属性不减反增（逐项开启，仍受原上限约束）', parent)
    for _, spec in ipairs(specs) do
      local r = toggle(statGroup, spec[2] .. '不减反增', 1024 + spec[1] * 4, 0)
      configValue(r, '变化倍率（1～1000）', 4096 + spec[1] * 4)
      r.Collapsed = true
    end
    local relation = toggle(parent, '好感度增长加倍', 32, 1)
    configValue(relation, '倍率（1～1000，默认10）', 36)
    enumRecord(configValue(relation, '好感下降转为增长', 40), '0:否\n1:是')
    local battle = group('战役设置（只识别主角／敌方／我方）', parent)
    toggle(battle, '我方人数不减', 68, 7)
    toggle(battle, '主角生命不减反增', 48, 5)
    toggle(battle, '主角内力不减反增', 52, 6)
    toggle(battle, '敌人内力快速清零（发生内力变化时）', 56, 6)
    local damage = toggle(battle, '对敌伤害加倍', 60, 5)
    configValue(damage, '伤害倍数（默认10）', 64)
    local combat = group('决斗设置', parent)
    toggle(combat, '主角生命不减反增', 80, 8)
    toggle(combat, '主角内力不减反增', 84, 9)
    toggle(combat, '敌人内力快速清零（发生内力变化时）', 88, 9)
    local combatDamage = toggle(combat, '对敌伤害加倍', 92, 8)
    configValue(combatDamage, '伤害倍数（默认10）', 96)
    local dice = toggle(parent, '设置天命轮盘点数', 112, 10)
    configValue(dice, '骰子点数（0～100，默认50）', 116)
    local status = group('补丁安装报告（入口检测，不代表已实战验证）', parent)
    local text = invokeBridge('Report')
    for line in tostring(text):gmatch('[^\r\n]+') do group(line, status) end
    parent.Collapsed = false
  end)
  if not ok then stopScripts(); error(err, 0) end
end

function T.disableExtensions()
  stopScripts()
  for _, r in ipairs(E.records) do
    -- Config descendants may already have been destroyed by stopScripts.
    if r and not r.IsGroupHeader then pcall(function() r.Active = false end) end
  end
  E.records, E.fieldCache, E.bridge = {}, {}, nil
  E.scripts = nil
end
function T.extend(root, context)
  E.records, E.fieldCache = {}, {}
  connectMono(true)
  local managerFields = mapFor(context.manager)
  local timeParent = top('游戏时间（修改可能改变剧情触发）', root)
  local timeDataOff = offset(managerFields, '_gameTimeData')
  local timeData = pointer(context.manager + timeDataOff, '游戏时间')
  local timeOff = offset(mapFor(timeData), 'GameTime')
  local time = pointer(timeData + timeOff, '当前日期')
  local tf = mapFor(time)
  for _, spec in ipairs({{'Year', '第几年'}, {'Month', '月份'}, {'Stage', '阶段'}, {'DayEnvironment', '环境'}}) do
    local r = valueRecord(timeParent, spec[2], context.instanceSlot, {timeDataOff, timeOff, offset(tf, spec[1])})
    local maximum = ({Year = 2147483647, Month = 12, Stage = 3, DayEnvironment = 2})[spec[1]]
    T.expose(r, {id = 'time:' .. spec[1], name = spec[2], section = 'time', category = 'time', kind = 'time', minimum = 1, maximum = maximum, note = '修改时间可能影响剧情触发。'})
    if spec[1] == 'Stage' then enumRecord(r, '1:上旬\n2:中旬\n3:下旬') end
    if spec[1] == 'DayEnvironment' then enumRecord(r, '1:白天\n2:晚上') end
  end
  local relationParent = top('人际关系', root)
  collectionRecords(relationParent, context.instanceSlot, context.manager, '_relationships', 'relationship')
  local inventory = top('物品数量（含未持有物品）', root)
  local dbClass = need(mono_findClass('Mortal.Core', 'ItemDatabase'), '找不到 ItemDatabase。')
  local dbf = fields(dbClass)
  local dbSlot = need(mono_class_getStaticFieldAddress(0, dbClass), '物品数据库未初始化。') + offset(dbf, '_instance')
  local db = pointer(dbSlot, '物品数据库')
  for _, spec in ipairs({{'_books', '书籍', 'book'}, {'_miscs', '杂项', 'misc'}, {'_special', '宝物', 'special'}, {'_consumables', '消耗品', 'consumable'}}) do
    collectionRecords(group(spec[2], inventory), dbSlot, db, spec[1], spec[3])
  end
  inventory.Collapsed = true
  E.scripts = top('启用脚本增强（默认关闭；其下功能需逐项勾选）', root)
  E.scripts.Options = '[moHideChildren]'
  E.scripts.OnActivate = function(record, before)
    if before then
      local ok, err = pcall(startScripts, record)
      if not ok then record.Description = '增强启用失败：' .. tostring(err); return false end
    end
    return true
  end
  E.scripts.OnDeactivate = function(_, before)
    if before then stopScripts() end
    return true
  end
end
