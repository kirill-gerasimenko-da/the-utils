#!/usr/bin/env bb

(ns publish
  (:require [babashka.process :refer [process check]]))

(defn run [dir args]
  (-> (process args {:dir dir :err :inherit :out :inherit})
      (check)
      :exit))

(run "."
  ["dotnet" "build"
   "./src/TheUtils/TheUtils.csproj"
   "-c" "Release"])

(run "."
  ["dotnet" "build"
   "./src/TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj"
   "-c" "Release"])

(run "."
  ["dotnet" "pack"
   "./src/TheUtils/TheUtils.csproj"
   "-c" "Release"
   "-o" "./publish"
   "/p:PackageVersion=0.0.7"])

(run "."
  ["dotnet" "pack"
   "./src/TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj"
   "-c" "Release"
   "-o" "./publish"
   "/p:PackageVersion=0.0.17"])
