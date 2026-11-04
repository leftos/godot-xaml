@tool
extends RefCounted

## A loopback client of the godot-xaml preview server: JSON-RPC 2.0 requests with Content-Length
## framing over StreamPeerTCP, polled once a frame of the given tree.

const WAIT_LIMIT_MS := 60000

var _tree: SceneTree
var _peer := StreamPeerTCP.new()
var _buffer := PackedByteArray()


func _init(tree: SceneTree) -> void:
	_tree = tree


## Connects to 127.0.0.1 at the port; returns "" once connected, else why not.
func open(port: int) -> String:
	var opened := _peer.connect_to_host("127.0.0.1", port)
	if opened != OK:
		return "connect_to_host(127.0.0.1, %d) returned %s" % [port, error_string(opened)]
	var start := Time.get_ticks_msec()
	_peer.poll()
	while _peer.get_status() == StreamPeerTCP.STATUS_CONNECTING:
		if Time.get_ticks_msec() - start > WAIT_LIMIT_MS:
			return "timed out connecting to port %d" % port
		await _tree.process_frame
		_peer.poll()
	if _peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		return "the connection to port %d ended in status %d" % [port, _peer.get_status()]
	return ""


## Sends a request and returns its reply; {"error": <why>} when the connection closes or fails, or
## no reply comes in time. Messages with another id are dropped.
func request(id: int, method: String, params: Dictionary) -> Dictionary:
	var message := {"jsonrpc": "2.0", "id": id, "method": method, "params": params}
	var body := JSON.stringify(message).to_utf8_buffer()
	var framed := ("Content-Length: %d\r\n\r\n" % body.size()).to_ascii_buffer()
	framed.append_array(body)
	var sent := _peer.put_data(framed)
	if sent != OK:
		return {"error": "sending %s %d failed: %s" % [method, id, error_string(sent)]}
	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start <= WAIT_LIMIT_MS:
		var reply := _read()
		if reply.has("error") and not reply.has("id"):
			return reply
		if not reply.is_empty() and reply.get("id") == id:
			return reply
		if reply.is_empty():
			if _peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
				return {"error": "connection closed before the reply to %s %d" % [method, id]}
			await _tree.process_frame
	return {"error": "timed out waiting for the reply to %s %d" % [method, id]}


func close() -> void:
	_peer.disconnect_from_host()


# The next whole message, {} while none is complete, or {"error": <why>} when a read fails.
func _read() -> Dictionary:
	_peer.poll()
	var available := _peer.get_available_bytes()
	if available > 0:
		var got := _peer.get_data(available)
		if got[0] != OK:
			return {"error": "reading from the server failed: %s" % error_string(got[0])}
		_buffer.append_array(got[1])
	var header_end := _blank_line_at(_buffer)
	if header_end < 0:
		return {}
	var header := _buffer.slice(0, header_end).get_string_from_ascii()
	var length := int(header.get_slice(":", 1).strip_edges())
	var body_start := header_end + 4
	if _buffer.size() < body_start + length:
		return {}
	var body := _buffer.slice(body_start, body_start + length).get_string_from_utf8()
	_buffer = _buffer.slice(body_start + length)
	var parsed = JSON.parse_string(body)
	return parsed if parsed is Dictionary else {"error": "a body that is no JSON object"}


# The offset of the first CR LF CR LF, or -1.
func _blank_line_at(bytes: PackedByteArray) -> int:
	for i in range(bytes.size() - 3):
		if bytes[i] == 13 and bytes[i + 1] == 10 and bytes[i + 2] == 13 and bytes[i + 3] == 10:
			return i
	return -1
