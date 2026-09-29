extends SceneTree

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		push_error("Usage: inspect-pck.gd <pack-path> <inventory-path>")
		quit(2)
		return
	if not ProjectSettings.load_resource_pack(args[0], true):
		push_error("Unable to load exported PCK")
		quit(1)
		return
	var entries: Array[String] = []
	_collect("res://", entries)
	entries.sort()
	var output := FileAccess.open(args[1], FileAccess.WRITE)
	if output == null:
		push_error("Unable to write PCK inventory")
		quit(1)
		return
	output.store_string(JSON.stringify(entries, "  "))
	quit()

func _collect(path: String, entries: Array[String]) -> void:
	var directory := DirAccess.open(path)
	if directory == null:
		return
	directory.list_dir_begin()
	var name := directory.get_next()
	while not name.is_empty():
		if name != "." and name != "..":
			var child := path.path_join(name)
			if directory.current_is_dir():
				_collect(child, entries)
			else:
				entries.append(child)
		name = directory.get_next()
	directory.list_dir_end()
