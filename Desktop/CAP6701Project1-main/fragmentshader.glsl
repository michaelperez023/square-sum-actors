#version 400

out vec3 Color;
in vec4 teColor;

void main() {
	//Color = vec3(teColor.rgb);
	Color = vec3(0.0, 1.0, 0.0);
}