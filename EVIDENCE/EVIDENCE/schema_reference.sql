-- SELECT VERSION();
VERSION()
4.1.22-community-nt

-- SHOW DATABASES;
Database
print_konvert

-- USE print_konvert;

-- SHOW TABLES;
ERROR 1046 (3D000) at line 1: No database selected

-- SHOW CREATE TABLE *
Table	Create Table
addrfrom	CREATE TABLE `addrfrom` (\n  `id` int(11) NOT NULL auto_increment,\n  `ind_from` varchar(6) default NULL,\n  `str_from1` varchar(255) default NULL,\n  `str_from2` varchar(255) default NULL,\n  `str_from3` varchar(255) default NULL,\n  `str_from4` varchar(255) default NULL,\n  `str_from5` varchar(255) default NULL,\n  `str_from6` varchar(255) default NULL,\n  `logo` mediumblob,\n  `logo_info` varchar(255) default NULL,\n  `file_Ext` varchar(60) default NULL,\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=15 DEFAULT CHARSET=cp1251
Table	Create Table
addrto	CREATE TABLE `addrto` (\n  `id` int(11) NOT NULL auto_increment,\n  `ind_to` varchar(6) default NULL,\n  `group_id` tinyint(4) NOT NULL default '0',\n  `str_to1` varchar(255) default NULL,\n  `str_to2` varchar(255) default NULL,\n  `str_to3` varchar(255) default NULL,\n  `str_to4` varchar(255) default NULL,\n  `str_to5` varchar(255) default NULL,\n  `str_to6` varchar(255) default NULL,\n  `copyes` tinyint(2) NOT NULL default '1',\n  `vip` tinyint(1) NOT NULL default '0',\n  `prim` varchar(255) default NULL,\n  `date_send` timestamp NULL default CURRENT_TIMESTAMP,\n  `print_prim` tinyint(1) NOT NULL default '0',\n  PRIMARY KEY  (`id`),\n  KEY `group_id` (`group_id`)\n) ENGINE=MyISAM AUTO_INCREMENT=154 DEFAULT CHARSET=cp1251
Table	Create Table
addrto_history	CREATE TABLE `addrto_history` (\n  `id` int(11) NOT NULL auto_increment,\n  `date_print` datetime default NULL,\n  `ind_to` varchar(6) default NULL,\n  `str_to1` varchar(255) default NULL,\n  `str_to2` varchar(255) default NULL,\n  `str_to3` varchar(255) default NULL,\n  `str_to4` varchar(255) default NULL,\n  `str_to5` varchar(255) default NULL,\n  `str_to6` varchar(255) default NULL,\n  `vip` smallint(1) NOT NULL default '0',\n  `copyes` smallint(2) NOT NULL default '1',\n  `prim` varchar(255) default NULL,\n  `teplate_descr` varchar(255) default NULL,\n  `print_prim` tinyint(1) NOT NULL default '0',\n  `id_env` int(11) NOT NULL default '0',\n  `addrfrom` mediumtext,\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=24 DEFAULT CHARSET=cp1251
Table	Create Table
addrblob	CREATE TABLE `addrblob` (\n  `id` int(11) NOT NULL auto_increment,\n  `img_qrcode` mediumblob,\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=2 DEFAULT CHARSET=cp1251
Table	Create Table
groups	CREATE TABLE `groups` (\n  `id` int(11) NOT NULL auto_increment,\n  `name` varchar(255) default NULL,\n  `prim` varchar(255) default NULL,\n  `fdefault` tinyint(1) NOT NULL default '0',\n  `closed` tinyint(1) NOT NULL default '0',\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=9 DEFAULT CHARSET=cp1251
Table	Create Table
inventory	CREATE TABLE `inventory` (\n  `id` int(11) NOT NULL auto_increment,\n  `id_main_to` int(11) NOT NULL default '0',\n  `name` mediumtext,\n  `komu` mediumtext,\n  `kuda` mediumtext,\n  `prim` mediumtext,\n  `date_create` timestamp NOT NULL default CURRENT_TIMESTAMP,\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=9 DEFAULT CHARSET=cp1251
Table	Create Table
inventory_detail	CREATE TABLE `inventory_detail` (\n  `id` int(11) NOT NULL auto_increment,\n  `id_main` int(11) NOT NULL default '0',\n  `name` mediumtext,\n  `kol` tinyint(4) NOT NULL default '0',\n  `cost` decimal(10,2) NOT NULL default '0.00',\n  `prim` mediumtext,\n  `date_create` timestamp NOT NULL default CURRENT_TIMESTAMP,\n  PRIMARY KEY  (`id`)\n) ENGINE=MyISAM AUTO_INCREMENT=10 DEFAULT CHARSET=cp1251
Table	Create Table
params	CREATE TABLE `params` (\n  `id` smallint(5) unsigned NOT NULL auto_increment,\n  `name` varchar(50) NOT NULL default '',\n  `value` varchar(255) default NULL,\n  PRIMARY KEY  (`id`),\n  KEY `name` (`name`)\n) ENGINE=MyISAM AUTO_INCREMENT=14 DEFAULT CHARSET=cp1251

-- row estimates
ERROR 1146 (42S02) at line 1: Table 'information_schema.tables' doesn't exist
