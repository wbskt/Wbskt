# syntax=docker/dockerfile:1
# The Angular console lives in a separate repo. Build context for this image is this directory
# (deploy/console/); before `docker compose build console`, produce a production build of the
# console app and copy/symlink its output into deploy/console/dist/, e.g.:
#   ng build --configuration production
#   cp -r <console-repo>/dist/<project>/browser deploy/console/dist
FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY dist/ /usr/share/nginx/html/
EXPOSE 80
