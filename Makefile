all: build clean

build:
	dotnet build -o ./ipk24chat-client

clean:
	dotnet clean