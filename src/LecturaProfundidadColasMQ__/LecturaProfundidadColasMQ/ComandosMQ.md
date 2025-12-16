
Opción A (recomendada): usar un usuario de servicio mqapp

(Windows) crear usuario local para el canal (no hace falta que sea admin ni mqm):

net user mqapp Passw0rd /add


Configurar el canal y CHLAUTH para usar ese usuario (el refresh security puede fallar):

runmqsc MQGD
ALTER CHANNEL(CHANNEL1) CHLTYPE(SVRCONN) MCAUSER('mqapp')
SET CHLAUTH(CHANNEL1) TYPE(ADDRESSMAP) ADDRESS('*') USERSRC(CHANNEL) ACTION(REPLACE)
REFRESH SECURITY TYPE(CHLAUTH)
end


Otorgar permisos al QM y colas para mqapp:

setmqaut -m MQGD -t qmgr  -p mqapp +connect +inq

setmqaut -m MQGD -t queue -n BNA.TU5.PEDIDO    -p mqapp +put +get +inq +browse
setmqaut -m MQGD -t queue -n BNA.TU5.RESPUESTA -p mqapp +put +get +inq +browse
setmqaut -m MQGD -t queue -n BNA.XX1.PEDIDO    -p mqapp +put +get +inq +browse
setmqaut -m MQGD -t queue -n BNA.XX1.RESPUESTA -p mqapp +put +get +inq +browse

rem (opcional) perfiles genéricos para cualquier BNA.*.PEDIDO/RESPUESTA
setmqaut -m MQGD -t queue -n BNA.*.PEDIDO     -p mqapp +put +get +inq +browse
setmqaut -m MQGD -t queue -n BNA.*.RESPUESTA  -p mqapp +put +get +inq +browse


Verificar que quedó otorgado:

dspmqaut -m MQGD -t qmgr  -p mqapp
dspmqaut -m MQGD -t queue -n BNA.TU5.PEDIDO -p mqapp


Tu código .NET no necesita user/pass; con CHANNEL1 ya entra como mqapp.


Tips importantes

Si conectás por cliente (SVRCONN), MQ no usa el usuario de Windows del equipo remoto por defecto; usa el MCAUSER del canal o lo que mapee CHLAUTH.
Si en tu canal tenés MCAUSER('mqapp'), entonces otorgá permisos a mqapp (no a I7-8VA\basilio).
Estos cambios no requieren REFRESH (aplican al instante). Solo usá REFRESH SECURITY TYPE(CHLAUTH) si cambiaste reglas CHLAUTH.
Para “solo lectura sin consumir”, reemplazá +get por +browse.