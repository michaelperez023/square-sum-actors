/*
 *  Project 1
 *  Authors: Kyle Lo, Michael Perez, Aotian Wu
 *	Adapted from sample code by author: Ruijin Wu <ruijin@cise.ufl.edu>
 *  CAP6701 - Dr. Jorg Peters
*/

#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <math.h>
#include <iostream>
#include <algorithm>
#include <GL/glew.h>
#include <GL/freeglut.h>
#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>

using namespace std;

#define WINDOW_TITLE_PREFIX "Advanced Graphics Project 1"

typedef struct {
	float XYZW[4];
	float RGBA[4];
} Vertex;

int CurrentWidth = 600, CurrentHeight = 600, WindowHandle = 0;

unsigned FrameCount = 0;

float TessLevelOuter = 30.0f;

// A deg 3 patch which needs k = deg + num_cpts + 1 = 8 knots
float Knots[8] = { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f };

glm::mat4 ModelMatrix;
glm::mat4 ViewMatrix;
glm::mat4 ProjectionMatrix;

GLuint VertexShaderId, FragmentShaderId, TessControlShaderId, TessEvalShaderId, ProgramId, VaoId, BufferId, IndexBufferId;

GLuint TessLevelOuterLocation, KnotsLocation;

GLuint ModelMatrixLocation, ViewMatrixLocation, ProjectionMatrixLocation;

void Initialize(int, char*[]);
void ResizeFunction(int, int);
void RenderFunction(void);
void TimerFunction(int);
void IdleFunction(void);
void KeyboardFunction(unsigned char, int, int);
void Cleanup(void);
void CreateVBO(void);
void DestroyVBO(void);
void CreateShaders(void);
void DestroyShaders(void);

int main(int argc, char* argv[]) {
	Initialize(argc, argv);

	glutMainLoop();
	
	exit(EXIT_SUCCESS);
}

void Initialize(int argc, char* argv[]) {
	GLenum GlewInitResult;

	glutInit(&argc, argv);

	// Init Context	
	glutInitContextVersion(4, 0);
//	glutInitContextFlags(GLUT_FORWARD_COMPATIBLE);
	glutInitContextProfile(GLUT_CORE_PROFILE);

	glutSetOption(
		GLUT_ACTION_ON_WINDOW_CLOSE,
		GLUT_ACTION_GLUTMAINLOOP_RETURNS
	);
	
	// Init Window
	glutInitWindowSize(CurrentWidth, CurrentHeight);

	glutInitDisplayMode(GLUT_DEPTH | GLUT_DOUBLE | GLUT_RGBA);

	WindowHandle = glutCreateWindow(WINDOW_TITLE_PREFIX);

	if(WindowHandle < 1) {
		fprintf(stderr, "ERROR: Could not create a new rendering window.\n");
		exit(EXIT_FAILURE);
	}
	
	// set callbacks
	glutReshapeFunc(ResizeFunction);
	glutDisplayFunc(RenderFunction);
	glutIdleFunc(IdleFunction);
	glutTimerFunc(0, TimerFunction, 0);
	glutCloseFunc(Cleanup);
	glutKeyboardFunc(KeyboardFunction);
	glewExperimental = GL_TRUE;
	GlewInitResult = glewInit();

	if (GLEW_OK != GlewInitResult) {
		fprintf(stderr, "ERROR: %s\n", glewGetErrorString(GlewInitResult));
		exit(EXIT_FAILURE);
	}
	
	fprintf(stdout, "INFO: OpenGL Version: %s\n", glGetString(GL_VERSION));

	// Init OpenGL
	glPolygonMode( GL_FRONT_AND_BACK, GL_LINE );
    glEnable(GL_DEPTH_TEST);

	// Init Shaders and Vertices
	CreateShaders();
	CreateVBO();

	// Init Matrix	
	ModelMatrix = glm::mat4(1.0);	// Identity Matrix

	ViewMatrix = glm::lookAt(glm::vec3(0,0.0f,5.0f),	// eye
							glm::vec3(0,0,0.0f),	// center
							glm::vec3(0,1.0,0.0f)	// up
				);

    ProjectionMatrix = glm::perspective(45.0f, 3.0f / 3.0f, 0.1f, 100.f);

	glClearColor(0.0f, 0.0f, 0.0f, 0.0f);
}

void KeyboardFunction(unsigned char Key, int X, int Y) {
	X; Y; // Resolves warning C4100: unreferenced formal parameter

	switch (Key) {
	case '1':
		TessLevelOuter++;
		break;
	case '2':
		TessLevelOuter--;
		break;
	default:
		break;
	}
}

void ResizeFunction(int Width, int Height) {
	CurrentWidth = Width;
	CurrentHeight = Height;
	glViewport(0, 0, CurrentWidth, CurrentHeight);
}

void RenderFunction(void) {
	++FrameCount;

	glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
	
	// update tessellation level
	glUniform1f(TessLevelOuterLocation, TessLevelOuter);

	// update knots
	glUniform1fv(KnotsLocation, 8, Knots);

	// update Matrix
	glUniformMatrix4fv(ModelMatrixLocation, 1, GL_FALSE, &(ModelMatrix[0][0]));
	glUniformMatrix4fv(ViewMatrixLocation, 1, GL_FALSE, &(ViewMatrix[0][0]));
	glUniformMatrix4fv(ProjectionMatrixLocation, 1, GL_FALSE, &(ProjectionMatrix[0][0]));

	// specify the patch type
	glPatchParameteri(GL_PATCH_VERTICES, 4); // degree + 1 vertices per patch

	// draw patch
	glDrawElements(GL_PATCHES, 24, GL_UNSIGNED_BYTE, NULL);

	glutSwapBuffers();
	glutPostRedisplay();
}

void IdleFunction(void) {
	glutPostRedisplay();
}

void TimerFunction(int Value) {	
	FrameCount = 0;

	// update scene
	
	// reset timer
	glutTimerFunc(10, TimerFunction, 1);
}

void Cleanup(void) {
	DestroyShaders();
	DestroyVBO();
}

void CreateVBO(void) {
	Vertex ControlPoints[] = {
		{ { 1.0f, -0.2f, 1.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } }, // 0
		{ { -0.6732f, 0.766f, -0.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } }, // 1
		{ { -0.6732f, -0.766f, 1.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } }, // 2
		{ { 1.0f, 0.2f, -0.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } }, // 3
		{ { -0.3268f, 0.966f, 1.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } }, // 4
		{ { -0.3268f, -0.966f, -0.4f, 1.0f }, { 1.0f, 0.0f, 0.0f, 1.0f } } // 5
	};

	GLubyte Indices[] = {
		0, 1, 2, 3,
        1, 2, 3, 4,
        2, 3, 4, 5,
		3, 4, 5, 0, 
		4, 5, 0, 1,
		5, 0, 1, 2
	};

	GLenum ErrorCheckValue = glGetError();
	const size_t BufferSize = sizeof(ControlPoints);
	const size_t VertexSize = sizeof(ControlPoints[0]);
	const size_t RgbOffset = sizeof(ControlPoints[0].XYZW);
	
	// Create Vertex Array Object
	glGenVertexArrays(1, &VaoId);
	glBindVertexArray(VaoId);
	
	// Create Buffer for vertex data
	glGenBuffers(1, &BufferId);
	glBindBuffer(GL_ARRAY_BUFFER, BufferId);
	glBufferData(GL_ARRAY_BUFFER, BufferSize, ControlPoints, GL_STATIC_DRAW);

	// Assign vertex attributes
	glVertexAttribPointer(0, 4, GL_FLOAT, GL_FALSE, VertexSize, 0);
	glVertexAttribPointer(1, 4, GL_FLOAT, GL_FALSE, VertexSize, (GLvoid*)RgbOffset);

	glEnableVertexAttribArray(0);
	glEnableVertexAttribArray(1);
	
	// Create Buffer for indices
	glGenBuffers(1, &IndexBufferId);
	glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, IndexBufferId);
	glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(Indices), Indices, GL_STATIC_DRAW);

	ErrorCheckValue = glGetError();
	if (ErrorCheckValue != GL_NO_ERROR) {
		fprintf(stderr, "ERROR: Could not create a VBO: %s \n", gluErrorString(ErrorCheckValue)	);
		exit(-1);
	}
}

void DestroyVBO(void) {
	GLenum ErrorCheckValue = glGetError();

	glDisableVertexAttribArray(1);
	glDisableVertexAttribArray(0);
	
	glBindBuffer(GL_ARRAY_BUFFER, 0);
	glDeleteBuffers(1, &BufferId);

	glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
	glDeleteBuffers(2, &IndexBufferId);

	glBindVertexArray(0);
	glDeleteVertexArrays(1, &VaoId);

	ErrorCheckValue = glGetError();
	if (ErrorCheckValue != GL_NO_ERROR) {
		fprintf(stderr, "ERROR: Could not destroy the VBO: %s \n", gluErrorString(ErrorCheckValue));
		exit(-1);
	}
}

GLuint LoadShader(const char* filename, GLenum shader_type) {
	GLuint shader_id = 0;
	FILE* file;
	long file_size = -1;
	char* glsl_source;

	fprintf(stderr, "compiling %s.\n", filename);
	if (NULL != (file = fopen(filename, "rb")) && 0 == fseek(file, 0, SEEK_END) &&	-1 != (file_size = ftell(file))) {
		rewind(file);
		
		if (NULL != (glsl_source = (char*)malloc(file_size + 1))) {
			if (file_size == (long)fread(glsl_source, sizeof(char), file_size, file)) {
				glsl_source[file_size] = '\0';

				if (0 != (shader_id = glCreateShader(shader_type))) {
					const char *sources[] = {glsl_source}; 
					glShaderSource(shader_id, 1, sources, NULL);
					glCompileShader(shader_id);
					GLuint ErrorCheckValue = glGetError();
					if (ErrorCheckValue != GL_NO_ERROR) {
						fprintf(stderr, "ERROR: Could not compile a shader: %s \n",gluErrorString(ErrorCheckValue));
						exit(-1);
					}
				}
				else
					fprintf(stderr, "ERROR: Could not create a shader.\n");
			}
			else
				fprintf(stderr, "ERROR: Could not read file %s\n", filename);

			free(glsl_source);
		}
		else
			fprintf(stderr, "ERROR: Could not allocate %i bytes.\n", file_size);

		fclose(file);
	}
	else
		fprintf(stderr, "ERROR: Could not open file %s\n", filename);

	return shader_id;
}

void CreateShaders(void) {
	GLchar log[2014];
	GLenum ErrorCheckValue = glGetError();

	VertexShaderId = LoadShader("vertexshader.glsl", GL_VERTEX_SHADER);	
	FragmentShaderId = LoadShader("fragmentshader.glsl", GL_FRAGMENT_SHADER); 
	TessControlShaderId = LoadShader("tcshader.glsl", GL_TESS_CONTROL_SHADER); 
	TessEvalShaderId = LoadShader("teshader.glsl", GL_TESS_EVALUATION_SHADER); 

	ProgramId = glCreateProgram();
	glAttachShader(ProgramId, VertexShaderId);
	glAttachShader(ProgramId, FragmentShaderId);
	glAttachShader(ProgramId, TessControlShaderId);
	glAttachShader(ProgramId, TessEvalShaderId);

	glLinkProgram(ProgramId);
	ErrorCheckValue = glGetError();

	if (ErrorCheckValue != GL_NO_ERROR) {
		glGetProgramInfoLog(ProgramId,1023,NULL,log);
		fprintf(stderr, "ERROR: Could not destroy the VBO: %s \n", log);
		exit(-1);
	}

	glUseProgram(ProgramId);

	TessLevelOuterLocation = glGetUniformLocation(ProgramId, "TessLevelOuter");

	KnotsLocation = glGetUniformLocation(ProgramId, "Knots");

	ModelMatrixLocation = glGetUniformLocation(ProgramId, "ModelMatrix");
	ViewMatrixLocation = glGetUniformLocation(ProgramId, "ViewMatrix");
	ProjectionMatrixLocation = glGetUniformLocation(ProgramId, "ProjectionMatrix");

	ErrorCheckValue = glGetError();
	if (ErrorCheckValue != GL_NO_ERROR) {
		glGetProgramInfoLog(ProgramId,1023,NULL,log);
		fprintf(stderr, "ERROR: Could not create the shaders: %s \n", log);
		exit(-1);
	}
}

void DestroyShaders(void) {
	GLenum ErrorCheckValue = glGetError();

	glUseProgram(0);

	glDetachShader(ProgramId, VertexShaderId);
	glDetachShader(ProgramId, FragmentShaderId);

	glDeleteShader(FragmentShaderId);
	glDeleteShader(VertexShaderId);

	glDeleteProgram(ProgramId);

	ErrorCheckValue = glGetError();
	if (ErrorCheckValue != GL_NO_ERROR) {
		fprintf(stderr, "ERROR: Could not destroy the shaders: %s \n", gluErrorString(ErrorCheckValue));
		exit(-1);
	}
}
