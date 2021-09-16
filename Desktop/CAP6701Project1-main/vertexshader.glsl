#version 400

layout(location = 0) in vec4 in_Position;
layout(location = 1) in vec4 in_Color;

void main(void) {
	gl_Position = in_Position;
	//vColor = in_Color;
}
